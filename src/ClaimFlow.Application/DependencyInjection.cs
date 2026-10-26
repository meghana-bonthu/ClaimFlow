using System.Security.Cryptography;
using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Adjudication.Rules;
using ClaimFlow.Application.Pricing;
using ClaimFlow.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimFlowApplication(this IServiceCollection services, AdjudicationOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<ClaimPricingCalculator>();

        // Adding a rule = one class + one line here. The engine discovers them via IEnumerable<IAdjudicationRule>.
        services.AddSingleton<IAdjudicationRule, DiagnosisCodeRule>();
        services.AddSingleton<IAdjudicationRule, MemberEligibilityRule>();
        services.AddSingleton<IAdjudicationRule, ProviderEligibilityRule>();
        services.AddSingleton<IAdjudicationRule, TimelyFilingRule>();
        services.AddSingleton<IAdjudicationRule, ProcedureCoverageRule>();
        services.AddSingleton<IAdjudicationRule, PriorAuthorizationRule>();
        services.AddSingleton<IAdjudicationRule, DuplicateServiceRule>();
        services.AddSingleton<IAdjudicationRule, HighDollarReviewRule>();
        services.AddSingleton<AdjudicationEngine>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IClaimNumberGenerator, RandomClaimNumberGenerator>();

        services.AddScoped<ClaimIntakeService>();
        services.AddScoped<AdjudicationService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<ClaimQueryService>();
        return services;
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>CLM-yyMMdd-XXXXXX using an unambiguous alphabet; uniqueness is enforced by a database index.</summary>
public sealed class RandomClaimNumberGenerator(IClock clock) : IClaimNumberGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public string Next()
    {
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++)
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"CLM-{clock.UtcNow:yyMMdd}-{suffix}";
    }
}
