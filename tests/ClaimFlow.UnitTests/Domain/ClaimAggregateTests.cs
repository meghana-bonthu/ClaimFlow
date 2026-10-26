using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Common;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.UnitTests.Domain;

public class ClaimAggregateTests
{
    [Fact]
    public void Create_sets_received_status_totals_and_history()
    {
        var claim = TestData.Claim(lines: [TestData.Line(billed: 150m), TestData.Line("80053", billed: 40.555m)]);

        Assert.Equal(ClaimStatus.Received, claim.Status);
        Assert.Equal(190.56m, claim.TotalBilled); // billed amounts are rounded to cents
        Assert.Equal([1, 2], claim.Lines.Select(l => l.LineNumber));
        var history = Assert.Single(claim.History);
        Assert.Equal(ClaimStatus.Received, history.ToStatus);
    }

    [Fact]
    public void Create_normalizes_and_deduplicates_diagnosis_codes()
    {
        var claim = TestData.Claim(dx: ["e11.9", "E119", " i10 "]);
        Assert.Equal(["E119", "I10"], claim.DiagnosisCodes);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    [InlineData(-1, 100)]
    public void Create_rejects_invalid_units_or_amounts(int units, decimal billed)
    {
        Assert.Throws<DomainException>(() => TestData.Claim(lines: [TestData.Line(units: units, billed: billed)]));
    }

    [Fact]
    public void Create_rejects_future_service_dates()
    {
        var ex = Assert.Throws<DomainException>(() => TestData.Claim(lines: [TestData.Line(daysAgo: -2)]));
        Assert.Contains("future", ex.Message);
    }

    [Fact]
    public void Create_requires_a_diagnosis()
    {
        Assert.Throws<DomainException>(() => TestData.Claim(dx: []));
    }

    [Fact]
    public void Finalize_with_one_payable_line_approves_claim_and_sums_amounts()
    {
        var claim = TestData.Claim(lines: [TestData.Line(), TestData.Line("80053")]).InAdjudication();

        claim.Finalize(
        [
            LineDecision.Approve(1, new LinePricing(92m, 0m, 25m, 0m, 67m)),
            LineDecision.Deny(2, ReasonCodes.NonCoveredService)
        ], TestData.Now, "test");

        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(92m, claim.TotalAllowed);
        Assert.Equal(67m, claim.PlanPaid);
        Assert.Equal(25m, claim.MemberResponsibility);
        Assert.Equal(ClaimLineStatus.Denied, claim.Lines[1].Status);
        Assert.Equal("96", claim.Lines[1].DenialCode);
    }

    [Fact]
    public void Finalize_with_all_lines_denied_denies_claim()
    {
        var claim = TestData.Claim().InAdjudication();
        claim.Finalize([LineDecision.Deny(1, ReasonCodes.Duplicate)], TestData.Now, "test");
        Assert.Equal(ClaimStatus.Denied, claim.Status);
        Assert.Equal(0m, claim.PlanPaid);
    }

    [Fact]
    public void Finalize_requires_a_decision_for_every_line()
    {
        var claim = TestData.Claim(lines: [TestData.Line(), TestData.Line("80053")]).InAdjudication();
        Assert.Throws<DomainException>(() =>
            claim.Finalize([LineDecision.Deny(1, ReasonCodes.Duplicate)], TestData.Now, "test"));
    }

    [Fact]
    public void Cannot_pay_a_claim_that_is_not_approved()
    {
        var claim = TestData.Claim();
        Assert.Throws<DomainException>(() => claim.MarkPaid("EFT1", TestData.Now, "test"));
    }

    [Fact]
    public void Cannot_begin_adjudication_twice()
    {
        var claim = TestData.Claim().InAdjudication();
        Assert.Throws<DomainException>(() => claim.BeginAdjudication(TestData.Now, "test"));
    }

    [Fact]
    public void Full_lifecycle_records_ordered_history_and_rotates_version()
    {
        var claim = TestData.Claim();
        var v0 = claim.Version;

        claim.BeginAdjudication(TestData.Now, "system");
        claim.Pend("Needs review", TestData.Now, "system");
        claim.Finalize([LineDecision.Approve(1, new LinePricing(92m, 0m, 25m, 0m, 67m))], TestData.Now, "reviewer");
        claim.MarkPaid("EFT1-0001", TestData.Now, "finance");

        Assert.Equal(ClaimStatus.Paid, claim.Status);
        Assert.Equal("EFT1-0001", claim.PaymentReference);
        Assert.Null(claim.PendReason);
        Assert.NotEqual(v0, claim.Version);
        Assert.Equal(
            [ClaimStatus.Received, ClaimStatus.InAdjudication, ClaimStatus.Pended, ClaimStatus.Approved, ClaimStatus.Paid],
            claim.History.OrderBy(h => h.Sequence).Select(h => h.ToStatus));
    }
}

public class NpiValidatorTests
{
    [Theory]
    [InlineData("1234567893", true)]  // CMS published example
    [InlineData("1982736450", true)]
    [InlineData("1234567890", false)] // bad check digit
    [InlineData("123456789", false)]  // too short
    [InlineData("12345678901", false)]
    [InlineData("12345A7893", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_check_digit_and_format(string? npi, bool expected) =>
        Assert.Equal(expected, NpiValidator.IsValid(npi));

    [Fact]
    public void Provider_rejects_invalid_npi() =>
        Assert.Throws<DomainException>(() => new Provider("1234567890", "Bad", "x", true));
}

public class MemberTests
{
    [Fact]
    public void Coverage_window_is_inclusive()
    {
        var member = TestData.Member(coverageStart: new DateOnly(2026, 1, 1), coverageEnd: new DateOnly(2026, 6, 30));
        Assert.True(member.IsCoveredOn(new DateOnly(2026, 1, 1)));
        Assert.True(member.IsCoveredOn(new DateOnly(2026, 6, 30)));
        Assert.False(member.IsCoveredOn(new DateOnly(2025, 12, 31)));
        Assert.False(member.IsCoveredOn(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void Accumulators_reset_for_a_new_plan_year_and_never_roll_back()
    {
        var member = TestData.Member();
        member.ApplyAccumulators(2026, new(300m, 350m));

        Assert.Equal(new(300m, 350m), member.GetAccumulators(2026));
        Assert.Equal(new(0m, 0m), member.GetAccumulators(2027));

        member.ApplyAccumulators(2025, new(10m, 10m)); // late prior-year claim must not overwrite current year
        Assert.Equal(new(300m, 350m), member.GetAccumulators(2026));
    }
}
