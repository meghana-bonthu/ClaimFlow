using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Procedure codes must be valid CPT/HCPCS (CARC 16) and present on the fee schedule (CARC 96).</summary>
public sealed class ProcedureCoverageRule : IAdjudicationRule
{
    public string Name => "ProcedureCoverage";
    public int Order => 50;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        foreach (var line in context.Claim.Lines)
        {
            if (!CodeFormats.ProcedureCode().IsMatch(line.ProcedureCode))
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.MissingOrInvalidInfo,
                    $"'{line.ProcedureCode}' is not a valid CPT/HCPCS code.");
            else if (!context.FeeSchedule.ContainsKey(line.ProcedureCode))
                yield return RuleFinding.DenyLine(Name, line.LineNumber, ReasonCodes.NonCoveredService,
                    $"Procedure {line.ProcedureCode} is not a covered benefit.");
        }
    }
}
