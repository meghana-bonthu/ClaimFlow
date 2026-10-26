using ClaimFlow.Domain.Common;
using ClaimFlow.Domain.Plans;

namespace ClaimFlow.Domain.Members;

/// <summary>An enrolled member (patient). Tracks plan-year accumulators used for cost sharing.</summary>
public sealed class Member : Entity
{
    private Member() { }

    public Member(string memberNumber, string firstName, string lastName, DateOnly dateOfBirth,
        HealthPlan plan, DateOnly coverageStart, DateOnly? coverageEnd = null)
    {
        if (string.IsNullOrWhiteSpace(memberNumber)) throw new DomainException("Member number is required.");
        if (coverageEnd is not null && coverageEnd < coverageStart)
            throw new DomainException("Coverage end date precedes start date.");

        MemberNumber = memberNumber.ToUpperInvariant();
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Plan = plan;
        PlanId = plan.Id;
        CoverageStart = coverageStart;
        CoverageEnd = coverageEnd;
        AccumulatorYear = coverageStart.Year;
    }

    public string MemberNumber { get; private set; } = default!;
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public DateOnly DateOfBirth { get; private set; }
    public Guid PlanId { get; private set; }
    public HealthPlan? Plan { get; private set; }
    public DateOnly CoverageStart { get; private set; }
    public DateOnly? CoverageEnd { get; private set; }

    public int AccumulatorYear { get; private set; }
    public decimal DeductibleMet { get; private set; }
    public decimal OutOfPocketMet { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public bool IsCoveredOn(DateOnly date) =>
        date >= CoverageStart && (CoverageEnd is null || date <= CoverageEnd);

    /// <summary>Returns accumulators for the plan year; a new plan year starts at zero.</summary>
    public PlanAccumulators GetAccumulators(int planYear) =>
        planYear == AccumulatorYear ? new(DeductibleMet, OutOfPocketMet) : new(0m, 0m);

    public void ApplyAccumulators(int planYear, PlanAccumulators updated)
    {
        if (planYear < AccumulatorYear) return; // prior-year claims never roll current accumulators back
        AccumulatorYear = planYear;
        DeductibleMet = updated.DeductibleMet;
        OutOfPocketMet = updated.OutOfPocketMet;
    }

    public void TerminateCoverage(DateOnly endDate)
    {
        if (endDate < CoverageStart) throw new DomainException("Termination date precedes coverage start.");
        CoverageEnd = endDate;
    }
}

public readonly record struct PlanAccumulators(decimal DeductibleMet, decimal OutOfPocketMet);
