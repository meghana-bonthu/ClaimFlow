using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Inactive providers are denied (CARC B7); out-of-network providers are held for review (CARC 242).</summary>
public sealed class ProviderEligibilityRule(AdjudicationOptions options) : IAdjudicationRule
{
    public string Name => "ProviderEligibility";
    public int Order => 30;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        var provider = context.Provider;
        if (!provider.IsActive)
        {
            yield return RuleFinding.DenyClaim(Name, ReasonCodes.ProviderNotEligible,
                $"Billing provider NPI {provider.Npi} is not active.");
            yield break;
        }

        if (!provider.IsInNetwork && options.PendOutOfNetwork)
            yield return RuleFinding.PendClaim(Name, ReasonCodes.OutOfNetworkReview,
                $"Billing provider NPI {provider.Npi} is out of network; requires review.");
    }
}
