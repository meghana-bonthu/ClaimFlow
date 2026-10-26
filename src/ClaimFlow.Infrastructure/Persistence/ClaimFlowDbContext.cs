using ClaimFlow.Domain.Audit;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaimFlow.Infrastructure.Persistence;

public sealed class ClaimFlowDbContext(DbContextOptions<ClaimFlowDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimLine> ClaimLines => Set<ClaimLine>();
    public DbSet<AdjudicationFinding> Findings => Set<AdjudicationFinding>();
    public DbSet<ClaimStatusChange> StatusHistory => Set<ClaimStatusChange>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<HealthPlan> Plans => Set<HealthPlan>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<FeeScheduleEntry> FeeSchedule => Set<FeeScheduleEntry>();
    public DbSet<PriorAuthorization> PriorAuthorizations => Set<PriorAuthorization>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AppUser> Users => Set<AppUser>();

    public bool IsSqlite => Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClaimFlowDbContext).Assembly);

        // Ids are assigned in the domain (Guid.NewGuid), so EF must not treat a set key as "already persisted".
        // Without this, child rows added to a tracked aggregate (findings, status history) are sent as UPDATEs.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey()?.Properties.Single();
            if (key is not null) key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<string>().HaveMaxLength(256);

        if (IsSqlite)
        {
            // SQLite has no decimal or datetimeoffset type, which blocks SUM/ORDER BY in SQL.
            // Doubles are fine for this demo provider; SQL Server keeps exact decimal(18,2).
            builder.Properties<decimal>().HaveConversion<double>();
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
        else
        {
            builder.Properties<decimal>().HavePrecision(18, 2);
        }
    }
}

/// <summary>Portal user. Passwords are stored as ASP.NET Core Identity PBKDF2 hashes.</summary>
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string Role { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
}
