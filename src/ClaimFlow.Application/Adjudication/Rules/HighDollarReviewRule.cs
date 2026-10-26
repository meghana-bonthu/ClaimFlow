using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Claims billed at or above the configured threshold are pended for a human reviewer.</summary>
public sealed class HighDollarReviewRule(AdjudicationOptions options) : IAdjudicationRule
{
    public string Name => "HighDollarReview";
    public int Order => 80;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        if (context.Claim.TotalBilled >= options.HighDollarReviewThreshold)
            yield return RuleFinding.PendClaim(Name, ReasonCodes.ManualReview,
                $"Billed ${context.Claim.TotalBilled:N2} meets the ${options.HighDollarReviewThreshold:N2} high-dollar review threshold.");
    }
}
