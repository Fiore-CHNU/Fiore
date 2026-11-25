using Fiore.Api.Domain.Catalog;
using Fiore.Api.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fiore.Api.Infrastructure.Persistence;

/// <summary>
/// Idempotent development data seeder for catalog facets, bouquets, inventory, and demo staff accounts.
/// </summary>
public static class FioreDbSeeder
{
    public static async Task SeedAsync(
        FioreDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken = default)
    {
        await SeedStaffUsersAsync(dbContext, passwordHasher, cancellationToken);
        var facetsBySlug = await SeedFacetsAsync(dbContext, cancellationToken);
        await SeedProductsAsync(dbContext, facetsBySlug, cancellationToken);
    }

    private static async Task SeedStaffUsersAsync(
        FioreDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        CancellationToken cancellationToken)
    {
        var existingRoles = await dbContext.Roles.ToDictionaryAsync(role => role.Code, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var demoStaff = new (string Email, string FirstName, string LastName, string RoleCode, string Password)[]
        {
            ("admin@fiore.local", "Олена", "Адмін", "administrator", "FioreAdmin123!"),
            ("manager@fiore.local", "Максим", "Менеджер", "manager", "FioreManager123!")
        };

        var added = false;
        foreach (var entry in demoStaff)
        {
            if (await dbContext.Users.AnyAsync(user => user.Email == entry.Email, cancellationToken))
            {
                continue;
            }

            if (!existingRoles.TryGetValue(entry.RoleCode, out var role))
            {
                continue;
            }

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = entry.Email,
                FirstName = entry.FirstName,
                LastName = entry.LastName,
                Status = UserAccountStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, entry.Password);
            user.Roles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                AssignedAt = now
            });
            dbContext.Users.Add(user);
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<Dictionary<(ProductFacetKind Kind, string Slug), CatalogFacet>> SeedFacetsAsync(
        FioreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var definitions = new (ProductFacetKind Kind, string Slug, string Name)[]
        {
            (ProductFacetKind.Occasion, "birthday", "День народження"),
            (ProductFacetKind.Occasion, "romantic", "Романтика"),
            (ProductFacetKind.Occasion, "wedding", "Весілля"),
            (ProductFacetKind.Occasion, "gratitude", "Подяка"),
            (ProductFacetKind.FlowerType, "roses", "Троянди"),
            (ProductFacetKind.FlowerType, "peonies", "Півонії"),
            (ProductFacetKind.FlowerType, "tulips", "Тюльпани"),
            (ProductFacetKind.FlowerType, "hydrangeas", "Гортензії"),
            (ProductFacetKind.Color, "red", "Червоний"),
            (ProductFacetKind.Color, "pink", "Рожевий"),
            (ProductFacetKind.Color, "white", "Білий"),
            (ProductFacetKind.Color, "pastel", "Пастельний")
        };

        var existing = await dbContext.CatalogFacets.ToListAsync(cancellationToken);
        var map = existing.ToDictionary(facet => (facet.Kind, facet.Slug));

        var added = false;
        foreach (var (kind, slug, name) in definitions)
        {
            if (map.ContainsKey((kind, slug)))
            {
                continue;
            }

            var facet = new CatalogFacet
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Slug = slug,
                Name = name
            };
            dbContext.CatalogFacets.Add(facet);
            map[(kind, slug)] = facet;
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return map;
    }

    private static async Task SeedProductsAsync(
        FioreDbContext dbContext,
        Dictionary<(ProductFacetKind Kind, string Slug), CatalogFacet> facetsByKey,
        CancellationToken cancellationToken)
    {
        var existingSkus = (await dbContext.Products.Select(product => product.Sku).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var catalog = new[]
        {
            CreateBouquet(
                "FIO-101",
                "Червоний оксамит",
                "Класичний букет із 25 преміальних червоних троянд для особливих моментів.",
                1850m,
                12,
                55m,
                38m,
                now.AddDays(-5),
                "https://images.unsplash.com/photo-1518709268805-4e9042af9f23?auto=format&fit=crop&w=900&q=80",
                [("Троянда Еквадор", 25, "Висота стебла 60 см"), ("Евкаліпт", 5, null)],
                [(ProductFacetKind.Occasion, "romantic"), (ProductFacetKind.FlowerType, "roses"), (ProductFacetKind.Color, "red")],
                facetsByKey),
            CreateBouquet(
                "FIO-102",
                "Ніжність півоній",
                "Повітряний пастельний букет із півоній та кущових троянд у дизайнерському пакуванні.",
                2200m,
                8,
                45m,
                35m,
                now.AddDays(-4),
                "https://images.unsplash.com/photo-1561181286-d3fee7d55364?auto=format&fit=crop&w=900&q=80",
                [("Півонія Сара Бернар", 9, null), ("Кущова троянда", 7, null)],
                [(ProductFacetKind.Occasion, "birthday"), (ProductFacetKind.FlowerType, "peonies"), (ProductFacetKind.Color, "pink")],
                facetsByKey),
            CreateBouquet(
                "FIO-103",
                "Весняний подих",
                "Свіжі біло-рожеві тюльпани з ароматною зеленню — легкий комплімент на щодень.",
                980m,
                15,
                40m,
                28m,
                now.AddDays(-3),
                "https://images.unsplash.com/photo-1520763185298-1b434c919102?auto=format&fit=crop&w=900&q=80",
                [("Тюльпан голландський", 19, null), ("Фісташка", 4, null)],
                [(ProductFacetKind.Occasion, "gratitude"), (ProductFacetKind.FlowerType, "tulips"), (ProductFacetKind.Color, "pastel")],
                facetsByKey),
            CreateBouquet(
                "FIO-104",
                "Біла хмара",
                "Об'ємна композиція з білих гортензій та еустоми для весілля або ювілею.",
                2650m,
                5,
                50m,
                42m,
                now.AddDays(-2),
                "https://images.unsplash.com/photo-1487530811176-3780de880c2d?auto=format&fit=crop&w=900&q=80",
                [("Гортензія біла", 3, null), ("Еустома", 9, null), ("Евкаліпт", 4, null)],
                [(ProductFacetKind.Occasion, "wedding"), (ProductFacetKind.FlowerType, "hydrangeas"), (ProductFacetKind.Color, "white")],
                facetsByKey),
            CreateBouquet(
                "FIO-105",
                "Ранкова роса",
                "Пастельний мікс троянд та півоній з ніжним ароматом.",
                1590m,
                10,
                48m,
                34m,
                now.AddDays(-1),
                "https://images.unsplash.com/photo-1455659817273-f96807779a8a?auto=format&fit=crop&w=900&q=80",
                [("Троянда піоноподібна", 11, null), ("Півонія", 5, null)],
                [(ProductFacetKind.Occasion, "birthday"), (ProductFacetKind.FlowerType, "roses"), (ProductFacetKind.Color, "pastel")],
                facetsByKey),
            CreateBouquet(
                "FIO-106",
                "Королівський руж",
                "Авторський букет із рідкісних сортів півоній та троянд (тимчасово закінчився).",
                3100m,
                0,
                58m,
                44m,
                now,
                "https://images.unsplash.com/photo-1526047932273-341f2a7631f9?auto=format&fit=crop&w=900&q=80",
                [("Півонія Корал Шарм", 15, null), ("Троянда Ред Наомі", 11, null)],
                [(ProductFacetKind.Occasion, "romantic"), (ProductFacetKind.FlowerType, "peonies"), (ProductFacetKind.Color, "red")],
                facetsByKey)
        };

        var added = false;
        foreach (var product in catalog)
        {
            if (existingSkus.Contains(product.Sku))
            {
                continue;
            }

            dbContext.Products.Add(product);
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static Product CreateBouquet(
        string sku,
        string name,
        string description,
        decimal price,
        int quantityAvailable,
        decimal heightCm,
        decimal widthCm,
        DateTimeOffset timestamp,
        string imageUrl,
        (string FlowerName, short StemCount, string? Note)[] composition,
        (ProductFacetKind Kind, string Slug)[] facetKeys,
        Dictionary<(ProductFacetKind Kind, string Slug), CatalogFacet> facetsByKey)
    {
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Sku = sku,
            Name = name,
            Description = description,
            BasePrice = price,
            Currency = "UAH",
            Status = ProductStatus.Available,
            HeightCm = heightCm,
            WidthCm = widthCm,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            Inventory = new ProductInventory
            {
                ProductId = productId,
                QuantityAvailable = quantityAvailable,
                ReorderLevel = 3,
                UpdatedAt = timestamp
            },
            Images =
            [
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = imageUrl,
                    AltText = name,
                    SortOrder = 0,
                    CreatedAt = timestamp
                }
            ],
            Composition = composition.Select((item, index) => new ProductCompositionItem
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                FlowerName = item.FlowerName,
                StemCount = item.StemCount,
                Note = item.Note,
                SortOrder = checked((short)index)
            }).ToList()
        };

        foreach (var key in facetKeys)
        {
            if (facetsByKey.TryGetValue(key, out var facet))
            {
                product.Facets.Add(new ProductFacet
                {
                    ProductId = productId,
                    FacetId = facet.Id,
                    Facet = facet
                });
            }
        }

        return product;
    }
}
