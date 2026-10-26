using ClaimFlow.Domain.Common;

namespace ClaimFlow.Domain.Claims;

public sealed class ClaimLine : Entity
{
    private ClaimLine() { }

    internal ClaimLine(Guid claimId, int lineNumber, NewClaimLine input)
    {
        ClaimId = claimId;
        LineNumber = lineNumber;
        ProcedureCode = input.ProcedureCode.Trim().ToUpperInvariant();
        Modifier = string.IsNullOrWhiteSpace(input.Modifier) ? null : input.Modifier.Trim().ToUpperInvariant();
        ServiceDate = input.ServiceDate;
        Units = input.Units;
        BilledAmount = Money.Round(input.BilledAmount);
        Status = ClaimLineStatus.Pending;
    }

    public Guid ClaimId { get; private set; }
    public int LineNumber { get; private set; }
    public string ProcedureCode { get; private set; } = default!;
    public string? Modifier { get; private set; }
    public DateOnly ServiceDate { get; private set; }
    public int Units { get; private set; }
    public decimal BilledAmount { get; private set; }

    public ClaimLineStatus Status { get; private set; }
    public decimal AllowedAmount { get; private set; }
    public decimal DeductibleAmount { get; private set; }
    public decimal CopayAmount { get; private set; }
    public decimal CoinsuranceAmount { get; private set; }
    public decimal PlanPaid { get; private set; }
    public string? DenialCode { get; private set; }

    public decimal MemberResponsibility => DeductibleAmount + CopayAmount + CoinsuranceAmount;

    internal void Apply(LineDecision decision)
    {
        if (decision.Approved)
        {
            var p = decision.Pricing ?? throw new DomainException($"Line {LineNumber} approved without pricing.");
            Status = ClaimLineStatus.Approved;
            AllowedAmount = p.Allowed;
            DeductibleAmount = p.Deductible;
            CopayAmount = p.Copay;
            CoinsuranceAmount = p.Coinsurance;
            PlanPaid = p.PlanPaid;
            DenialCode = null;
        }
        else
        {
            Status = ClaimLineStatus.Denied;
            AllowedAmount = DeductibleAmount = CopayAmount = CoinsuranceAmount = PlanPaid = 0m;
            DenialCode = decision.DenialCode ?? ReasonCodes.NonCoveredService;
        }
    }
}

public sealed record NewClaimLine(string ProcedureCode, DateOnly ServiceDate, int Units, decimal BilledAmount, string? Modifier = null);

/// <summary>Priced amounts for an approved line. Invariant: Allowed = Deductible + Copay + Coinsurance + PlanPaid.</summary>
public sealed record LinePricing(decimal Allowed, decimal Deductible, decimal Copay, decimal Coinsurance, decimal PlanPaid);

public sealed record LineDecision(int LineNumber, bool Approved, LinePricing? Pricing, string? DenialCode)
{
    public static LineDecision Approve(int lineNumber, LinePricing pricing) => new(lineNumber, true, pricing, null);
    public static LineDecision Deny(int lineNumber, string code) => new(lineNumber, false, null, code);
}
