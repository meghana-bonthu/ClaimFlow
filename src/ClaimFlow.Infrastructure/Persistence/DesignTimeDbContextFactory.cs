using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimFlow.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` to generate SQL Server migrations. SQLite (local demo) uses EnsureCreated instead.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClaimFlowDbContext>
{
    public ClaimFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ClaimFlowDbContext>()
            .UseSqlServer("Server=localhost;Database=ClaimFlow;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new ClaimFlowDbContext(options);
    }
}
