# Fiore

Fiore is a flower e-commerce platform. This repository contains the product implementation.

## Technology stack

- Client: React, TypeScript, Vite, Tailwind CSS, shadcn/ui or MUI, Zustand or Redux Toolkit, TanStack Query, Axios, and React Router v6.
- API: ASP.NET Core Minimal API with Vertical Slice Architecture, MediatR, and FluentValidation.
- Data: PostgreSQL with EF Core and Npgsql.
- Authentication: JWT for customers and back-office staff when authentication work enters scope.

## Current implementation

Sprint I starts with `EAN-152`, the paginated public catalogue API.

`GET /api/v1/catalog/products?page=1&pageSize=12` returns publicly visible products in newest-first order with:

- product identifier, SKU, name, price, currency, and primary image;
- `isAvailable` from product-level inventory;
- pagination metadata: page, page size, total item count, and total page count.

Products marked `hidden` or `discontinued` are excluded. An out-of-stock product remains visible and is returned with `isAvailable: false`.

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
