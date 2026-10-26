using ClaimFlow.Application.Pricing;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;

namespace ClaimFlow.UnitTests.Pricing;

public class ClaimPricingCalculatorTests
{
    private readonly ClaimPricingCalculator _calculator = new();
    private readonly HealthPlan _plan = TestData.GoldPlan(); // $500 deductible, 20% coinsurance, $25 copay, $4,000 OOP max

    private static PricingInput Input(int line, FeeScheduleEntry fee, decimal billed, int units = 1, int day = 1) =>
        new(line, new DateOnly(2026, 3, day), units, billed, fee);

    private static void AssertBalanced(LinePricing p) =>
        Assert.Equal(p.Allowed, p.Deductible + p.Copay + p.Coinsurance + p.PlanPaid);

    [Fact]
    public void Office_visit_charges_copay_and_skips_deductible()
    {
        var result = _calculator.Price(_plan, new(0m, 0m), [Input(1, TestData.OfficeVisit, 150m)]);

        var p = result.Lines[1];
        Assert.Equal(new LinePricing(92m, 0m, 25m, 0m, 67m), p);
        Assert.Equal(new PlanAccumulators(0m, 25m), result.EndingAccumulators);
    }

    [Fact]
    public void Allowed_is_lesser_of_billed_and_fee_schedule_times_units()
    {
        var result = _calculator.Price(_plan, new(500m, 500m), [
            Input(1, TestData.Lab, billed: 100m, units: 3), // 14 x 3 = 42 < 100
            Input(2, TestData.Lab, billed: 10m)             // billed below fee schedule
        ]);

        Assert.Equal(42m, result.Lines[1].Allowed);
        Assert.Equal(10m, result.Lines[2].Allowed);
    }

    [Fact]
    public void Deductible_applies_first_then_coinsurance()
    {
        var result = _calculator.Price(_plan, new(0m, 0m), [Input(1, TestData.Mri, 1200m)]);

        // Allowed 420: all toward the $500 deductible, no coinsurance yet.
        Assert.Equal(new LinePricing(420m, 420m, 0m, 0m, 0m), result.Lines[1]);
    }

    [Fact]
    public void Deductible_carries_across_lines_in_service_date_order()
    {
        var result = _calculator.Price(_plan, new(0m, 0m), [
            Input(2, TestData.Mri, 1200m, day: 10),  // later service date, priced second
            Input(1, TestData.Mri, 1200m, day: 5)
        ]);

        Assert.Equal(new LinePricing(420m, 420m, 0m, 0m, 0m), result.Lines[1]);
        // Remaining deductible 80, then 20% of 340 = 68 coinsurance, plan pays 272.
        Assert.Equal(new LinePricing(420m, 80m, 0m, 68m, 272m), result.Lines[2]);
        Assert.Equal(new PlanAccumulators(500m, 568m), result.EndingAccumulators);
    }

    [Fact]
    public void Member_cost_share_is_capped_at_out_of_pocket_maximum()
    {
        // Deductible met; only $10 left before the $4,000 OOP max.
        var result = _calculator.Price(_plan, new(500m, 3990m), [Input(1, TestData.Mri, 1200m)]);

        var p = result.Lines[1];
        Assert.Equal(10m, p.Coinsurance);
        Assert.Equal(410m, p.PlanPaid);
        AssertBalanced(p);
        Assert.Equal(4000m, result.EndingAccumulators.OutOfPocketMet);
    }

    [Fact]
    public void Once_out_of_pocket_max_is_met_plan_pays_everything()
    {
        var result = _calculator.Price(_plan, new(500m, 4000m), [Input(1, TestData.OfficeVisit, 150m), Input(2, TestData.Mri, 500m)]);

        Assert.Equal(92m, result.Lines[1].PlanPaid);
        Assert.Equal(0m, result.Lines[1].Copay);
        Assert.Equal(420m, result.Lines[2].PlanPaid);
    }

    [Fact]
    public void Coinsurance_is_rounded_to_cents()
    {
        var fee = new FeeScheduleEntry("85025", "CBC", 11.11m);
        var result = _calculator.Price(_plan, new(500m, 500m), [Input(1, fee, 50m)]);

        Assert.Equal(2.22m, result.Lines[1].Coinsurance); // 11.11 * 0.20 = 2.222
        AssertBalanced(result.Lines[1]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(250, 250)]
    [InlineData(500, 1000)]
    [InlineData(500, 3999.99)]
    public void Every_priced_line_balances(decimal deductibleMet, decimal oopMet)
    {
        var result = _calculator.Price(_plan, new(deductibleMet, oopMet), [
            Input(1, TestData.OfficeVisit, 150m),
            Input(2, TestData.Lab, 33.33m, units: 2),
            Input(3, TestData.Mri, 999.99m)
        ]);

        foreach (var p in result.Lines.Values)
        {
            AssertBalanced(p);
            Assert.True(p.PlanPaid >= 0);
        }
        Assert.True(result.EndingAccumulators.OutOfPocketMet <= _plan.OutOfPocketMax);
    }
}
