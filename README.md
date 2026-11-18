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

`GET /api/v1/catalog/products/{productId}` returns the public product-details view used by the product page: description, ordered image gallery, price, currency, and availability. Hidden and discontinued products return `404`.

The catalogue also accepts optional UI filters: `search`, `minPrice`, `maxPrice`, `inStockOnly`, and `sort` (`newest`, `priceAscending`, or `priceDescending`).

## Guest cart and checkout API

The customer UI can use the following public flow:

1. `POST /api/v1/carts` creates a guest cart and returns its `id` and `sessionToken`.
2. Send the returned token as `X-Cart-Token` on `GET /api/v1/carts/{cartId}`, `POST /api/v1/carts/{cartId}/items`, `PUT /api/v1/carts/{cartId}/items/{productId}`, and `DELETE /api/v1/carts/{cartId}/items/{productId}`.
3. `POST /api/v1/checkout` receives the cart ID, customer contact information, delivery address, and a delivery time range. It validates availability, stores immutable product-price snapshots, and returns an order in `pendingPayment` status. `GET /api/v1/orders/{orderId}` retrieves that confirmation and current status with the same `X-Cart-Token`.

The current checkout does not deduct stock or redirect to a payment provider: reservation policy and the payment provider remain team decisions. This keeps the API honest until those integrations are approved.

## Accounts and back-office API

`POST /api/v1/auth/register` creates a customer account and returns a JWT. `POST /api/v1/auth/login` returns a JWT for an existing customer, manager, or administrator. Use `Authorization: Bearer <token>` for `GET /api/v1/auth/me`.

The protected back-office API is deliberately restricted to `manager` and `administrator` roles:

- `POST /api/v1/admin/products` creates a product, initial stock, and optional image gallery.
- `PUT /api/v1/admin/products/{productId}` changes product data, status, stock, and gallery.
- `GET /api/v1/admin/orders` lists orders for operational work.
- `PUT /api/v1/admin/orders/{orderId}/status` moves an order only through the supported process: `pendingPayment → paid → accepted → assembling → readyForDelivery → outForDelivery → delivered`. Cancellation is allowed before dispatch.

The first administrator is not seeded with a default password. For a one-time local or deployment bootstrap, set a secret `Bootstrap__AdministratorToken` and call `POST /api/v1/setup/administrator` with that secret in `X-Setup-Token`; it works only while no administrator exists. An administrator can then create managers or other administrators via `POST /api/v1/admin/users`.

Production requires a `Jwt__SigningKey` secret. The built-in development key is intentionally unavailable in the Production environment.

For the React/Vite client, CORS allows the default local development and preview origins: `http://localhost:5173` and `http://localhost:4173`. Add a production client URL through `Cors:AllowedOrigins` before deployment.

## Local setup

Prerequisites: .NET SDK 8, Docker Desktop, and Docker Compose.

```bash
docker compose up -d postgres
dotnet tool restore
dotnet ef database update --project src/Fiore.Api
dotnet run --project src/Fiore.Api --urls http://localhost:5090
```

Open Swagger at `http://localhost:5090/swagger` in the Development environment. Run the test suite with:

```bash
dotnet test
```

On Apple Silicon macOS with Homebrew, the local SDK is available at `/opt/homebrew/opt/dotnet@8/bin/dotnet` until `dotnet@8` is linked into `PATH`.
