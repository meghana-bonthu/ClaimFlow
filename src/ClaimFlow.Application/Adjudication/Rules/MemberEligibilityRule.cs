using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Each service date must fall within the member's coverage period (CARC 26 / 27).</summary>
public sealed class MemberEligibilityRule : IAdjudicationRule
{
    public string Name => "MemberEligibility";
    public int Order => 20;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        var member = context.Member;
        foreach (var line in context.Claim.Lines)
        {
            if (line.ServiceDate < member.CoverageStart)
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.BeforeCoverage,
                    $"Service date {line.ServiceDate:yyyy-MM-dd} is before coverage start {member.CoverageStart:yyyy-MM-dd}.");
            else if (!member.IsCoveredOn(line.ServiceDate))
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.AfterCoverageTerminated,
                    $"Service date {line.ServiceDate:yyyy-MM-dd} is after coverage ended {member.CoverageEnd:yyyy-MM-dd}.");
        }
    }
}
