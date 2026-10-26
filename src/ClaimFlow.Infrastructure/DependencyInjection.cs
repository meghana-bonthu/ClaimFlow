using ClaimFlow.Application.Abstractions;
using ClaimFlow.Infrastructure.Persistence;
using ClaimFlow.Infrastructure.Persistence.Repositories;
using ClaimFlow.Infrastructure.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimFlowInfrastructure(this IServiceCollection services, IConfiguration configuration, bool runWorker = true)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("ClaimFlow")
            ?? throw new InvalidOperationException("Connection string 'ClaimFlow' is not configured.");

        services.AddDbContext<ClaimFlowDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5));
            else
                options.UseSqlite(connectionString);
        });

        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<ChannelClaimQueue>();
        services.AddSingleton<IClaimQueue>(sp => sp.GetRequiredService<ChannelClaimQueue>());
        if (runWorker) services.AddHostedService<AdjudicationWorker>();

        services.AddHealthChecks().AddDbContextCheck<ClaimFlowDbContext>("database");
        return services;
    }
}
