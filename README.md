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
2. Send the returned token as `X-Cart-Token` on `GET /api/v1/carts/{cartId}`, `POST /api/v1/carts/{cartId}/items`, `PUT /api/v1/carts/{cartId}/items/{productId}`, and `DELETE /api/v1/carts/{cartId}/items/{productId}`. The cart returns `subtotalAmount`, `deliveryFeeAmount`, `totalAmount`, `minimumOrderAmount`, `meetsMinimumOrder`, `hasPriceChanges`, and `hasUnavailableItems`. Each item carries the current `unitPrice` and the `acceptedUnitPrice` the customer saw when adding it.
3. `GET /api/v1/delivery/slots?date=YYYY-MM-DD` returns `{ date, slots }`. Each slot has its remaining capacity and `isAvailable`, which is false when the slot is full or starts in less than 90 minutes (Kyiv time). Without `date`, the endpoint returns today, or tomorrow when nothing is left today.
4. `POST /api/v1/checkout` receives the cart ID, contact information, delivery address, a slot, an optional `cardMessage` (up to 250 characters), and `callBeforeDelivery`. Phones must match `+380XXXXXXXXX`; the city must be in `Delivery:ServiceCities`. Validation errors return `400` with field errors. Business conflicts return `409` with an `error` code and, where relevant, the affected `items`: `ProductNotAvailable`, `QuantityExceedsAvailability`, `PriceChanged`, `DeliverySlotUnavailable`, and `MinimumOrderNotMet`. After a `PriceChanged` conflict, the customer confirms the new prices with `POST /api/v1/carts/{cartId}/accept-prices`. A successful checkout reserves stock, fixes item prices, and returns the order with `orderNumber` and `trackingCode` in `pendingPayment` status.
5. `POST /api/v1/orders/{orderId}/payments/confirm` (and backward-compatible `/payments/sandbox/confirm`) charges through `IPaymentGateway` (`SandboxPaymentGateway` by default). In `Development`/`Testing`, `outcome=declined` (`409 PaymentDeclined`) or `outcome=providerUnavailable` (`502 PaymentProviderUnavailable`, `retryable: true`) can be passed as a query parameter or `X-Sandbox-Outcome` header to test failure handling and payment retries within the 15-minute window. A payment attempt after the window expires returns `409 PaymentWindowExpired`. `GET /api/v1/orders/{orderId}` retrieves the confirmation, payment state, and status timeline with the same `X-Cart-Token`.
6. `GET /api/v1/orders/track/{trackingCode}` is the public order-status page for the confirmation link. It shows the order number, items, slot, and status history, but no contact or address data.

Business rules from the Jira backlog are configuration defaults: unpaid orders expire after 15 minutes with a check every minute (`Orders`); delivery costs UAH 100 below UAH 1500 and is free from UAH 1500, the minimum order is UAH 500, slots allow 10 orders and close 90 minutes before they start (`Delivery`). `Delivery:ServiceCities` defaults to Chernivtsi and is an assumption until the team confirms the service area.

`GET /api/v1/catalog/products/{productId}/similar?limit=4` returns orderable bouquets that share a colour facet, then those with the closest price.

The production payment provider is still a team decision. Replace the sandbox confirmation endpoint with a verified webhook from the selected provider (for example LiqPay) before any public deployment.

## Accounts and back-office API

`POST /api/v1/auth/register` creates a customer account and returns a JWT. `POST /api/v1/auth/login` returns a JWT for an existing customer, manager, or administrator. Five consecutive wrong passwords lock the account for five minutes; locked sign-ins return `423` with `retryAfterSeconds`. Use `Authorization: Bearer <token>` for `GET /api/v1/auth/me`.

The protected back-office API is deliberately restricted to `manager` and `administrator` roles:

- `POST /api/v1/admin/products` creates a product, initial stock, and optional image gallery.
- `PUT /api/v1/admin/products/{productId}` changes product data, dimensions, packaging, composition, filter facets, status, stock, and gallery.
- `GET /api/v1/admin/products?search=` lists all products for the back office, including hidden and discontinued ones, with status, stock, and availability.
- `PUT /api/v1/admin/products/{productId}/availability` with `{ "isAvailable": false }` is the one-click "out of stock" switch; the bouquet stays visible but cannot be ordered.
- `POST /api/v1/admin/catalog/facets` creates an occasion, flower-type, or colour facet used by the catalogue filters.
- `GET /api/v1/admin/orders?status=&deliveryDate=YYYY-MM-DD` is the order queue. Unpaid (`pendingPayment`) orders are hidden unless requested with `status=pendingPayment`.
- `GET /api/v1/admin/orders/{orderId}` returns the order card: contact and delivery data, greeting-card text, courier, payment, and status history with the staff member who made each change.
- `PUT /api/v1/admin/orders/{orderId}/courier` records the external courier (name, optional `+380` phone) while the order is assembling or ready for delivery; `GET /api/v1/admin/orders/{orderId}/delivery-summary` returns the delivery text to send to the courier.
- `PUT /api/v1/admin/orders/{orderId}/status` moves an order only through the supported process: `pendingPayment → paid → accepted → assembling → readyForDelivery → outForDelivery → delivered`. `outForDelivery` requires an assigned courier. Cancellation is allowed before dispatch.

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
dotnet run --project src/Fiore.Api --urls http://localhost:5162
```

Open Swagger at `http://localhost:5162/swagger` in the Development environment.

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
