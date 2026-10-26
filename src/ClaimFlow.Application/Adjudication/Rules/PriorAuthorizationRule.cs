using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Procedures flagged on the fee schedule need an authorization covering the service date (CARC 197).</summary>
public sealed class PriorAuthorizationRule : IAdjudicationRule
{
    public string Name => "PriorAuthorization";
    public int Order => 60;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        foreach (var line in context.Claim.Lines)
        {
            if (!context.FeeSchedule.TryGetValue(line.ProcedureCode, out var fee) || !fee.RequiresPriorAuthorization)
                continue;

            if (!context.PriorAuthorizations.Any(a => a.Covers(line.ProcedureCode, line.ServiceDate)))
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.PriorAuthAbsent,
                    $"Procedure {line.ProcedureCode} requires prior authorization; none found for {line.ServiceDate:yyyy-MM-dd}.");
        }
    }
}
