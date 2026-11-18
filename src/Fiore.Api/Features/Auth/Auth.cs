using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fiore.Api.Domain.Identity;
using Fiore.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Fiore.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string FirstName, string? LastName = null, string? Phone = null);
public sealed record LoginRequest(string Email, string Password);
public sealed record CreateStaffUserRequest(string Email, string Password, string FirstName, string? LastName, string? Phone, string Role);
public sealed record AuthResponse(Guid UserId, string Email, IReadOnlyList<string> Roles, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Password).MinimumLength(8).MaximumLength(128);
        RuleFor(request => request.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(request => request.LastName).MaximumLength(80).When(request => request.LastName is not null);
        RuleFor(request => request.Phone).MaximumLength(32).When(request => request.Phone is not null);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class CreateStaffUserRequestValidator : AbstractValidator<CreateStaffUserRequest>
{
    public CreateStaffUserRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Password).MinimumLength(8).MaximumLength(128);
        RuleFor(request => request.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(request => request.LastName).MaximumLength(80).When(request => request.LastName is not null);
        RuleFor(request => request.Phone).MaximumLength(32).When(request => request.Phone is not null);
        RuleFor(request => request.Role).Must(role => role is "manager" or "administrator")
            .WithMessage("Staff role must be manager or administrator.");
    }
}

public sealed record JwtOptions(string Issuer, string Audience, string SigningKey, int ExpiryMinutes);

public sealed class TokenService(JwtOptions options)
{
    public AuthResponse Create(AppUser user, IReadOnlyList<string> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(options.ExpiryMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256));

        return new AuthResponse(user.Id, user.Email, roles, new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auth/register", async (RegisterRequest request, IValidator<RegisterRequest> validator, FioreDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, TokenService tokenService, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var email = request.Email.Trim().ToLowerInvariant();
            if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
            {
                return Results.Conflict(new { error = "EmailAlreadyRegistered" });
            }

            var customerRole = await dbContext.Roles.SingleAsync(role => role.Code == "customer", cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName?.Trim(),
                Phone = request.Phone?.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = customerRole.Id, AssignedAt = now });
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            var response = tokenService.Create(user, ["customer"]);
            return Results.Created($"/api/v1/users/{user.Id}", response);
        }).AllowAnonymous().WithName("RegisterCustomer").WithOpenApi();

        endpoints.MapPost("/api/v1/auth/login", async (LoginRequest request, IValidator<LoginRequest> validator, FioreDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, TokenService tokenService, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await dbContext.Users.Include(candidate => candidate.Roles).ThenInclude(role => role.Role)
                .SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
            if (user is null || user.Status != UserAccountStatus.Active || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(tokenService.Create(user, user.Roles.Select(role => role.Role.Code).ToList()));
        }).AllowAnonymous().WithName("Login").WithOpenApi();

        endpoints.MapGet("/api/v1/auth/me", (ClaimsPrincipal principal) => Results.Ok(new
        {
            userId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
            email = principal.FindFirstValue(ClaimTypes.Email),
            roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
        })).RequireAuthorization().WithName("GetCurrentUser").WithOpenApi();

        endpoints.MapPost("/api/v1/setup/administrator", async (CreateStaffUserRequest request, HttpRequest httpRequest, IConfiguration configuration, IValidator<CreateStaffUserRequest> validator, FioreDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, TokenService tokenService, CancellationToken cancellationToken) =>
        {
            var bootstrapToken = configuration["Bootstrap:AdministratorToken"];
            if (string.IsNullOrWhiteSpace(bootstrapToken) || request.Role != "administrator") return Results.NotFound();
            if (!httpRequest.Headers.TryGetValue("X-Setup-Token", out var suppliedToken) || !string.Equals(suppliedToken, bootstrapToken, StringComparison.Ordinal)) return Results.Unauthorized();
            if (await dbContext.UserRoles.AnyAsync(role => role.Role.Code == "administrator", cancellationToken)) return Results.Conflict(new { error = "AdministratorAlreadyExists" });

            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            if (await dbContext.Users.AnyAsync(user => user.Email == request.Email.Trim().ToLowerInvariant(), cancellationToken)) return Results.Conflict(new { error = "EmailAlreadyRegistered" });

            var administratorRole = await dbContext.Roles.SingleAsync(role => role.Code == "administrator", cancellationToken);
            var user = CreateUser(request, administratorRole, passwordHasher);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", tokenService.Create(user, ["administrator"]));
        }).AllowAnonymous().WithName("BootstrapAdministrator").WithOpenApi();

        var administratorGroup = endpoints.MapGroup("/api/v1/admin/users").RequireAuthorization(policy => policy.RequireRole("administrator"));
        administratorGroup.MapPost("", async (CreateStaffUserRequest request, IValidator<CreateStaffUserRequest> validator, FioreDbContext dbContext, IPasswordHasher<AppUser> passwordHasher, CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var email = request.Email.Trim().ToLowerInvariant();
            if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken)) return Results.Conflict(new { error = "EmailAlreadyRegistered" });
            var role = await dbContext.Roles.SingleAsync(candidate => candidate.Code == request.Role, cancellationToken);
            var user = CreateUser(request, role, passwordHasher);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/users/{user.Id}", new { user.Id, user.Email, role = role.Code });
        }).WithName("CreateStaffUser").WithOpenApi();

        return endpoints;
    }

    private static AppUser CreateUser(CreateStaffUserRequest request, AppRole role, IPasswordHasher<AppUser> passwordHasher)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim().ToLowerInvariant(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName?.Trim(),
            Phone = request.Phone?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.Roles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = now });
        return user;
    }
}
