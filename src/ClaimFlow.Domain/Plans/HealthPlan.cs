using ClaimFlow.Domain.Common;

namespace ClaimFlow.Domain.Plans;

/// <summary>Benefit design used to price claims: deductible, copay, coinsurance and out-of-pocket maximum.</summary>
public sealed class HealthPlan : Entity
{
    private HealthPlan() { }

    public HealthPlan(string code, string name, decimal annualDeductible, decimal coinsuranceRate,
        decimal officeVisitCopay, decimal outOfPocketMax, int timelyFilingDays)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Plan code is required.");
        if (coinsuranceRate is < 0 or > 1) throw new DomainException("Coinsurance rate must be between 0 and 1.");
        if (annualDeductible < 0 || officeVisitCopay < 0 || outOfPocketMax < 0)
            throw new DomainException("Plan amounts cannot be negative.");
        if (timelyFilingDays <= 0) throw new DomainException("Timely filing days must be positive.");

        Code = code;
        Name = name;
        AnnualDeductible = annualDeductible;
        CoinsuranceRate = coinsuranceRate;
        OfficeVisitCopay = officeVisitCopay;
        OutOfPocketMax = outOfPocketMax;
        TimelyFilingDays = timelyFilingDays;
    }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public decimal AnnualDeductible { get; private set; }

    /// <summary>Member share after the deductible, e.g. 0.20 = member pays 20%.</summary>
    public decimal CoinsuranceRate { get; private set; }

    public decimal OfficeVisitCopay { get; private set; }
    public decimal OutOfPocketMax { get; private set; }
    public int TimelyFilingDays { get; private set; }
}

/// <summary>Contracted (allowed) amount for a procedure code.</summary>
public sealed class FeeScheduleEntry : Entity
{
    private FeeScheduleEntry() { }

    public FeeScheduleEntry(string procedureCode, string description, decimal allowedAmount,
        bool requiresPriorAuthorization = false, bool isOfficeVisit = false)
    {
        ProcedureCode = procedureCode.ToUpperInvariant();
        Description = description;
        AllowedAmount = allowedAmount;
        RequiresPriorAuthorization = requiresPriorAuthorization;
        IsOfficeVisit = isOfficeVisit;
    }

    public string ProcedureCode { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public decimal AllowedAmount { get; private set; }
    public bool RequiresPriorAuthorization { get; private set; }

    /// <summary>Office visits are priced with a flat copay instead of deductible + coinsurance.</summary>
    public bool IsOfficeVisit { get; private set; }
}

public sealed class PriorAuthorization : Entity
{
    private PriorAuthorization() { }

    public PriorAuthorization(Guid memberId, string procedureCode, string authorizationNumber, DateOnly validFrom, DateOnly validTo)
    {
        if (validTo < validFrom) throw new DomainException("Authorization end date precedes start date.");
        MemberId = memberId;
        ProcedureCode = procedureCode.ToUpperInvariant();
        AuthorizationNumber = authorizationNumber;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public Guid MemberId { get; private set; }
    public string ProcedureCode { get; private set; } = default!;
    public string AuthorizationNumber { get; private set; } = default!;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }

    public bool Covers(string procedureCode, DateOnly serviceDate) =>
        string.Equals(ProcedureCode, procedureCode, StringComparison.OrdinalIgnoreCase)
        && serviceDate >= ValidFrom && serviceDate <= ValidTo;
}
