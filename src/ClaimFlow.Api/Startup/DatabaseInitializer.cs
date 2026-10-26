using ClaimFlow.Api.Auth;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Infrastructure.Persistence;
using ClaimFlow.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Startup;

public static class DatabaseInitializer
{
    public static readonly (string Email, string Name, string Role, string Password)[] DemoUsers =
    [
        ("submitter@claimflow.dev", "Sam Submitter", Roles.Submitter, "Submit#2026"),
        ("adjudicator@claimflow.dev", "Alex Adjudicator", Roles.Adjudicator, "Review#2026"),
        ("admin@claimflow.dev", "Ada Admin", Roles.Admin, "Admin#2026")
    ];

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ClaimFlowDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        var time = sp.GetRequiredService<TimeProvider>();

        if (db.IsSqlite) await db.Database.EnsureCreatedAsync(ct);
        else await db.Database.MigrateAsync(ct);

        var now = time.GetUtcNow();
        await DataSeeder.SeedReferenceDataAsync(db, DateOnly.FromDateTime(now.UtcDateTime), ct);

        if (config.GetValue("Seed:DemoUsers", true) && !await db.Users.AnyAsync(ct))
        {
            var hasher = sp.GetRequiredService<IPasswordHasher<AppUser>>();
            foreach (var (email, name, role, password) in DemoUsers)
            {
                var user = new AppUser { Email = email, DisplayName = name, Role = role };
                user.PasswordHash = hasher.HashPassword(user, password);
                db.Users.Add(user);
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} demo users", DemoUsers.Length);
        }

        if (config.GetValue("Seed:DemoClaims", true))
        {
            await DataSeeder.SeedDemoClaimsAsync(db, sp.GetRequiredService<AdjudicationEngine>(), now, ct);
        }
    }
}
