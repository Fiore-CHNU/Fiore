# Fiore

Fiore is a flower e-commerce platform. This repository contains the product implementation.

## Technology stack

- Client: React, TypeScript, Vite, Tailwind CSS, shadcn/ui or MUI, Zustand or Redux Toolkit, TanStack Query, Axios, and React Router v6.
- API: ASP.NET Core Minimal API with Vertical Slice Architecture, MediatR, and FluentValidation.
- Data: PostgreSQL with EF Core and Npgsql.
- Authentication: JWT for customers and back-office staff.

## Current implementation

Sprint I starts with `EAN-152`, the paginated public catalogue API.

`GET /api/v1/catalog/products?page=1&pageSize=12` returns publicly visible products in newest-first order with:

- product identifier, SKU, name, price, currency, and primary image;
- `isAvailable` from product-level inventory;
- pagination metadata: page, page size, total item count, and total page count.

Products marked `hidden` or `discontinued` are excluded. An out-of-stock product remains visible and is returned with `isAvailable: false`.

`GET /api/v1/catalog/products/{productId}` returns the public product-details view used by the product page: description, ordered image gallery, price, currency, dimensions, bouquet composition, and filter facets. Hidden and discontinued products return `404`.

The catalogue also accepts optional UI filters: `search` (name, SKU, or composition), `minPrice`, `maxPrice`, `inStockOnly`, `occasion`, `flowerType`, `color`, and `sort` (`newest`, `priceAscending`, or `priceDescending`). `GET /api/v1/catalog/facets` supplies filter values; managers can maintain them through the back-office API.

## Guest cart and checkout API

The customer UI can use the following public flow:

1. `POST /api/v1/carts` creates a guest cart and returns its `id` and `sessionToken`.
2. Send the returned token as `X-Cart-Token` on `GET /api/v1/carts/{cartId}`, `POST /api/v1/carts/{cartId}/items`, `PUT /api/v1/carts/{cartId}/items/{productId}`, and `DELETE /api/v1/carts/{cartId}/items/{productId}`.
3. `GET /api/v1/delivery/slots?date=YYYY-MM-DD` returns configured two-hour slots and their remaining capacity. `POST /api/v1/checkout` receives the cart ID, contact information, delivery address, and a slot. It validates availability and capacity, reserves stock, stores immutable product-price snapshots, calculates delivery, and returns an order in `pendingPayment` status.
4. `POST /api/v1/orders/{orderId}/payments/sandbox/confirm` simulates a successful card payment for the current guest order. It is a local demo contract, not a payment-acquirer integration. `GET /api/v1/orders/{orderId}` retrieves the confirmation, payment state, and status timeline with the same `X-Cart-Token`.

Checkout reserves inventory immediately. Pending payments automatically expire after 30 minutes by default and restore stock; both the lifetime and check interval are configurable under `Orders`. Delivery is a fixed UAH fee with a free-delivery threshold and slot capacity, configured under `Delivery`.

The production payment provider is still a team decision. Replace the sandbox confirmation endpoint with a verified webhook from the selected provider (for example LiqPay) before any public deployment.

## Accounts and back-office API

`POST /api/v1/auth/register` creates a customer account and returns a JWT. `POST /api/v1/auth/login` returns a JWT for an existing customer, manager, or administrator. Use `Authorization: Bearer <token>` for `GET /api/v1/auth/me`.

The protected back-office API is deliberately restricted to `manager` and `administrator` roles:

- `POST /api/v1/admin/products` creates a product, initial stock, and optional image gallery.
- `PUT /api/v1/admin/products/{productId}` changes product data, dimensions, composition, filter facets, status, stock, and gallery.
- `POST /api/v1/admin/catalog/facets` creates an occasion, flower-type, or colour facet used by the catalogue filters.
- `GET /api/v1/admin/orders` lists orders for operational work.
- `PUT /api/v1/admin/orders/{orderId}/status` moves an order only through the supported process: `pendingPayment → paid → accepted → assembling → readyForDelivery → outForDelivery → delivered`. Cancellation is allowed before dispatch.

The first administrator is not seeded with a default password. For a one-time local or deployment bootstrap, set a secret `Bootstrap__AdministratorToken` and call `POST /api/v1/setup/administrator` with that secret in `X-Setup-Token`; it works only while no administrator exists. An administrator can then create managers or other administrators via `POST /api/v1/admin/users`.

Production requires a `Jwt__SigningKey` secret. The built-in development key is intentionally unavailable in the Production environment.

For the React/Vite client, CORS allows the default local development and preview origins: `http://localhost:5173` and `http://localhost:4173`. Add a production client URL through `Cors:AllowedOrigins` before deployment.

## Local setup

Prerequisites: .NET SDK 8 and Docker Desktop.

### Option 1: .NET Aspire (Recommended — manages PostgreSQL, Redis, migrations, and seed data automatically)

```bash
dotnet run --project src/Fiore.AppHost
```

Open the Aspire Dashboard URL printed in the console, or go directly to Swagger at `http://localhost:5162/swagger`.

### Option 2: Docker Compose

```bash
docker compose up -d
dotnet tool restore
dotnet ef database update --project src/Fiore.Api
dotnet run --project src/Fiore.Api --urls http://localhost:5090
```

Open Swagger at `http://localhost:5090/swagger` in the Development environment.

### Development seed accounts

When running in the `Development` environment against a relational database, `FioreDbSeeder` idempotently seeds demo catalogue facets, 6 bouquets, and staff accounts for local testing:

| Role | Email | Password |
|---|---|---|
| `administrator` | `admin@fiore.local` | `FioreAdmin123!` |
| `manager` | `manager@fiore.local` | `FioreManager123!` |

Run the test suite with:

```bash
dotnet test
```

On Apple Silicon macOS with Homebrew, the local SDK is available at `/opt/homebrew/opt/dotnet@8/bin/dotnet` until `dotnet@8` is linked into `PATH`.
