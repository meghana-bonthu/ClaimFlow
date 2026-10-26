using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Lines received after the plan's timely filing limit are denied (CARC 29).</summary>
public sealed class TimelyFilingRule : IAdjudicationRule
{
    public string Name => "TimelyFiling";
    public int Order => 40;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        var limit = context.Plan.TimelyFilingDays;
        foreach (var line in context.Claim.Lines)
        {
            var days = context.Claim.ReceivedDate.DayNumber - line.ServiceDate.DayNumber;
            if (days > limit)
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.TimelyFilingExpired,
                    $"Received {days} days after service; limit is {limit} days.");
        }
    }
}
