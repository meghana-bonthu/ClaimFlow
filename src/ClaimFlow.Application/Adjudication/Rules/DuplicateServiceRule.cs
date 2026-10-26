using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>
/// Same member + provider + procedure + modifier + service date already billed (on another claim or
/// earlier on this claim) is an exact duplicate (CARC 18).
/// </summary>
public sealed class DuplicateServiceRule : IAdjudicationRule
{
    public string Name => "DuplicateService";
    public int Order => 70;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        var seenOnThisClaim = new HashSet<(string, string?, DateOnly)>();

        foreach (var line in context.Claim.Lines)
        {
            var key = (line.ProcedureCode, line.Modifier, line.ServiceDate);

            var prior = context.ServiceHistory.FirstOrDefault(h =>
                h.ProcedureCode == line.ProcedureCode && h.Modifier == line.Modifier && h.ServiceDate == line.ServiceDate);

            if (prior is not null)
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.Duplicate,
                    $"Duplicate of {prior.ClaimNumber} ({line.ProcedureCode} on {line.ServiceDate:yyyy-MM-dd}).");
            else if (!seenOnThisClaim.Add(key))
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.Duplicate,
                    $"Duplicate of an earlier line on this claim ({line.ProcedureCode} on {line.ServiceDate:yyyy-MM-dd}).");
        }
    }
}
