using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication.Rules;

/// <summary>Every diagnosis code must be a well-formed ICD-10-CM code; otherwise the claim is rejected (CARC 16).</summary>
public sealed class DiagnosisCodeRule : IAdjudicationRule
{
    public string Name => "DiagnosisCodes";
    public int Order => 10;

    public IEnumerable<RuleFinding> Evaluate(AdjudicationContext context)
    {
        var invalid = context.Claim.DiagnosisCodes.Where(c => !CodeFormats.Icd10Cm().IsMatch(c)).ToList();
        if (invalid.Count > 0)
            yield return RuleFinding.DenyClaim(Name, ReasonCodes.MissingOrInvalidInfo,
                $"Invalid ICD-10-CM diagnosis code(s): {string.Join(", ", invalid)}.");
    }
}
