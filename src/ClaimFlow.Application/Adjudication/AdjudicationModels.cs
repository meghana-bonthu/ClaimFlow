using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.Application.Adjudication;

/// <summary>Everything a rule needs, loaded once so rules stay pure and synchronous (and trivially unit-testable).</summary>
public sealed record AdjudicationContext(
    Claim Claim,
    Member Member,
    HealthPlan Plan,
    Provider Provider,
    IReadOnlyDictionary<string, FeeScheduleEntry> FeeSchedule,
    IReadOnlyList<PriorAuthorization> PriorAuthorizations,
    IReadOnlyList<ExistingServiceLine> ServiceHistory);

/// <summary>A previously submitted, non-denied service line for the same member and provider.</summary>
public sealed record ExistingServiceLine(string ClaimNumber, string ProcedureCode, string? Modifier, DateOnly ServiceDate);

public sealed record RuleFinding(string RuleName, FindingOutcome Outcome, string? ReasonCode, string Message, int? LineNumber = null)
{
    public static RuleFinding DenyLine(string rule, int line, string code, string message) => new(rule, FindingOutcome.Deny, code, message, line);
    public static RuleFinding DenyClaim(string rule, string code, string message) => new(rule, FindingOutcome.Deny, code, message);
    public static RuleFinding PendClaim(string rule, string code, string message) => new(rule, FindingOutcome.Pend, code, message);
}

public interface IAdjudicationRule
{
    string Name { get; }

    /// <summary>Lower runs first. Order only affects how findings are listed; every rule always runs.</summary>
    int Order { get; }

    /// <summary>Returns only failures (Deny/Pend). The engine records a Pass when a rule returns nothing.</summary>
    IEnumerable<RuleFinding> Evaluate(AdjudicationContext context);
}

public sealed class AdjudicationOptions
{
    public decimal HighDollarReviewThreshold { get; set; } = 10_000m;
    public bool PendOutOfNetwork { get; set; } = true;
}

public sealed record AdjudicationEvaluation(IReadOnlyList<RuleFinding> Findings)
{
    public bool HasPends => Findings.Any(f => f.Outcome == FindingOutcome.Pend);

    public string PendSummary => string.Join("; ", Findings.Where(f => f.Outcome == FindingOutcome.Pend).Select(f => f.Message));
}

public sealed record AdjudicationDecision(IReadOnlyList<LineDecision> Lines, int PlanYear, PlanAccumulators EndingAccumulators)
{
    public bool AnyPayable => Lines.Any(l => l.Approved);
}
