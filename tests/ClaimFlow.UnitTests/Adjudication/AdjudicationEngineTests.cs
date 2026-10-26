using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Adjudication.Rules;
using ClaimFlow.Application.Pricing;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Plans;

namespace ClaimFlow.UnitTests.Adjudication;

public class AdjudicationEngineTests
{
    private static AdjudicationEngine Engine(AdjudicationOptions? options = null)
    {
        options ??= new AdjudicationOptions();
        IAdjudicationRule[] rules =
        [
            new HighDollarReviewRule(options), // registered out of order on purpose
            new DiagnosisCodeRule(),
            new MemberEligibilityRule(),
            new ProviderEligibilityRule(options),
            new TimelyFilingRule(),
            new ProcedureCoverageRule(),
            new PriorAuthorizationRule(),
            new DuplicateServiceRule()
        ];
        return new AdjudicationEngine(rules, new ClaimPricingCalculator());
    }

    [Fact]
    public void Clean_claim_passes_every_rule_in_order_and_is_priced()
    {
        var engine = Engine();
        var context = TestData.Context(TestData.Claim(lines: [TestData.Line(billed: 150m), TestData.Line("80053", billed: 40m)]));

        var evaluation = engine.Evaluate(context);
        var decision = engine.Decide(context, evaluation);

        Assert.All(evaluation.Findings, f => Assert.Equal(FindingOutcome.Pass, f.Outcome));
        Assert.Equal("DiagnosisCodes", evaluation.Findings[0].RuleName);
        Assert.Equal("HighDollarReview", evaluation.Findings[^1].RuleName);
        Assert.False(evaluation.HasPends);
        Assert.All(decision.Lines, l => Assert.True(l.Approved));
        Assert.Equal(25m, decision.Lines[0].Pricing!.Copay);
        Assert.Equal(14m, decision.Lines[1].Pricing!.Deductible); // lab goes to deductible
    }

    [Fact]
    public void Line_level_denial_only_affects_that_line()
    {
        var engine = Engine();
        var context = TestData.Context(TestData.Claim(lines: [TestData.Line(), TestData.Line("73721", billed: 1200m)]));

        var decision = engine.Decide(context, engine.Evaluate(context));

        Assert.True(decision.Lines[0].Approved);
        Assert.False(decision.Lines[1].Approved);
        Assert.Equal(ReasonCodes.PriorAuthAbsent, decision.Lines[1].DenialCode);
        Assert.True(decision.AnyPayable);
    }

    [Fact]
    public void Claim_level_denial_denies_every_line()
    {
        var engine = Engine();
        var provider = TestData.Provider(active: false);
        var context = TestData.Context(TestData.Claim(provider: provider, lines: [TestData.Line(), TestData.Line("80053")]), provider: provider);

        var decision = engine.Decide(context, engine.Evaluate(context));

        Assert.All(decision.Lines, l => Assert.Equal(ReasonCodes.ProviderNotEligible, l.DenialCode));
        Assert.False(decision.AnyPayable);
    }

    [Fact]
    public void Pend_findings_are_reported_with_a_summary()
    {
        var engine = Engine();
        var provider = TestData.Provider(inNetwork: false);
        var context = TestData.Context(TestData.Claim(provider: provider), provider: provider);

        var evaluation = engine.Evaluate(context);

        Assert.True(evaluation.HasPends);
        Assert.Contains("out of network", evaluation.PendSummary);
    }

    [Fact]
    public void Pricing_starts_from_member_accumulators()
    {
        var engine = Engine();
        var member = TestData.Member();
        member.ApplyAccumulators(TestData.Today.Year, new(500m, 500m)); // deductible met
        var context = TestData.Context(TestData.Claim(member, lines: [TestData.Line("80053", billed: 40m)]), member);

        var decision = engine.Decide(context, engine.Evaluate(context));

        Assert.Equal(new LinePricing(14m, 0m, 0m, 2.8m, 11.2m), decision.Lines[0].Pricing);
        Assert.Equal(new(500m, 502.8m), decision.EndingAccumulators);
    }

    [Fact]
    public void Line_without_fee_schedule_entry_is_denied_even_if_coverage_rule_is_absent()
    {
        var engine = new AdjudicationEngine([new DiagnosisCodeRule()], new ClaimPricingCalculator());
        var context = TestData.Context(TestData.Claim(lines: [TestData.Line("99499")]),
            fees: new Dictionary<string, FeeScheduleEntry>());

        var decision = engine.Decide(context, engine.Evaluate(context));

        Assert.Equal(ReasonCodes.NonCoveredService, Assert.Single(decision.Lines).DenialCode);
    }

    [Fact]
    public void DenyAll_denies_every_line_with_given_code()
    {
        var context = TestData.Context(TestData.Claim(lines: [TestData.Line(), TestData.Line("80053")]));
        var decision = AdjudicationEngine.DenyAll(context, ReasonCodes.NotMedicallyNecessary);
        Assert.All(decision.Lines, l => Assert.Equal("50", l.DenialCode));
    }
}
