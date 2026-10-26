using ClaimFlow.Application.Pricing;
using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Adjudication;

/// <summary>
/// Runs every registered rule (Strategy pattern) and turns the findings into per-line decisions.
/// A claim-level denial denies every line; a line-level denial denies only that line;
/// remaining lines are priced by <see cref="ClaimPricingCalculator"/>.
/// </summary>
public sealed class AdjudicationEngine(IEnumerable<IAdjudicationRule> rules, ClaimPricingCalculator pricing)
{
    private readonly IReadOnlyList<IAdjudicationRule> _rules = rules.OrderBy(r => r.Order).ToList();

    public IReadOnlyList<string> RuleNames => _rules.Select(r => r.Name).ToList();

    public AdjudicationEvaluation Evaluate(AdjudicationContext context)
    {
        var findings = new List<RuleFinding>();
        foreach (var rule in _rules)
        {
            var results = rule.Evaluate(context).ToList();
            findings.AddRange(results.Count > 0
                ? results
                : [new RuleFinding(rule.Name, FindingOutcome.Pass, null, "Passed")]);
        }

        return new AdjudicationEvaluation(findings);
    }

    public AdjudicationDecision Decide(AdjudicationContext context, AdjudicationEvaluation evaluation)
    {
        var denials = evaluation.Findings.Where(f => f.Outcome == FindingOutcome.Deny).ToList();
        var claimDenial = denials.FirstOrDefault(f => f.LineNumber is null);

        var decisions = new List<LineDecision>();
        var payable = new List<PricingInput>();

        foreach (var line in context.Claim.Lines)
        {
            var denial = claimDenial ?? denials.FirstOrDefault(f => f.LineNumber == line.LineNumber);
            if (denial is not null)
            {
                decisions.Add(LineDecision.Deny(line.LineNumber, denial.ReasonCode ?? ReasonCodes.NonCoveredService));
                continue;
            }

            // ProcedureCoverageRule normally denies these first; this keeps the engine safe if that rule is removed.
            if (!context.FeeSchedule.TryGetValue(line.ProcedureCode, out var fee))
            {
                decisions.Add(LineDecision.Deny(line.LineNumber, ReasonCodes.NonCoveredService));
                continue;
            }

            payable.Add(new PricingInput(line.LineNumber, line.ServiceDate, line.Units, line.BilledAmount, fee));
        }

        var planYear = context.Claim.ServiceDateFrom.Year;
        var start = context.Member.GetAccumulators(planYear);
        var priced = pricing.Price(context.Plan, start, payable);

        decisions.AddRange(priced.Lines.Select(kv => LineDecision.Approve(kv.Key, kv.Value)));
        return new AdjudicationDecision(decisions.OrderBy(d => d.LineNumber).ToList(), planYear, priced.EndingAccumulators);
    }

    /// <summary>Manual denial by a reviewer: every line denied with the chosen reason code.</summary>
    public static AdjudicationDecision DenyAll(AdjudicationContext context, string reasonCode) =>
        new(context.Claim.Lines.Select(l => LineDecision.Deny(l.LineNumber, reasonCode)).ToList(),
            context.Claim.ServiceDateFrom.Year,
            context.Member.GetAccumulators(context.Claim.ServiceDateFrom.Year));
}
