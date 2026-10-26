using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Adjudication.Rules;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Plans;

namespace ClaimFlow.UnitTests.Adjudication;

public class DiagnosisCodeRuleTests
{
    [Theory]
    [InlineData("J069")]
    [InlineData("E11.65")]
    [InlineData("S72001A")]
    [InlineData("Z0000")]
    public void Valid_icd10_codes_pass(string code)
    {
        var context = TestData.Context(TestData.Claim(dx: [code]));
        Assert.Empty(new DiagnosisCodeRule().Evaluate(context));
    }

    [Theory]
    [InlineData("12345")]   // numeric
    [InlineData("U071")]    // U codes are reserved
    [InlineData("J0")]      // too short
    [InlineData("J06999999")]
    public void Invalid_codes_deny_the_whole_claim(string code)
    {
        var finding = Assert.Single(new DiagnosisCodeRule().Evaluate(TestData.Context(TestData.Claim(dx: [code]))));
        Assert.Equal(FindingOutcome.Deny, finding.Outcome);
        Assert.Equal(ReasonCodes.MissingOrInvalidInfo, finding.ReasonCode);
        Assert.Null(finding.LineNumber);
    }
}

public class MemberEligibilityRuleTests
{
    [Fact]
    public void Service_before_coverage_is_denied_with_carc_26()
    {
        var member = TestData.Member(coverageStart: TestData.Today.AddDays(-3));
        var claim = TestData.Claim(member, lines: [TestData.Line(daysAgo: 10)]);

        var f = Assert.Single(new MemberEligibilityRule().Evaluate(TestData.Context(claim, member)));
        Assert.Equal(ReasonCodes.BeforeCoverage, f.ReasonCode);
        Assert.Equal(1, f.LineNumber);
    }

    [Fact]
    public void Service_after_termination_is_denied_with_carc_27_only_on_affected_lines()
    {
        var member = TestData.Member(coverageEnd: TestData.Today.AddDays(-7));
        var claim = TestData.Claim(member, lines: [TestData.Line(daysAgo: 10), TestData.Line("80053", daysAgo: 2)]);

        var f = Assert.Single(new MemberEligibilityRule().Evaluate(TestData.Context(claim, member)));
        Assert.Equal(ReasonCodes.AfterCoverageTerminated, f.ReasonCode);
        Assert.Equal(2, f.LineNumber);
    }
}

public class ProviderEligibilityRuleTests
{
    [Fact]
    public void Inactive_provider_denies_claim()
    {
        var provider = TestData.Provider(active: false);
        var f = Assert.Single(new ProviderEligibilityRule(new AdjudicationOptions()).Evaluate(TestData.Context(provider: provider)));
        Assert.Equal(FindingOutcome.Deny, f.Outcome);
        Assert.Equal(ReasonCodes.ProviderNotEligible, f.ReasonCode);
    }

    [Fact]
    public void Out_of_network_provider_pends_claim()
    {
        var f = Assert.Single(new ProviderEligibilityRule(new AdjudicationOptions()).Evaluate(TestData.Context(provider: TestData.Provider(inNetwork: false))));
        Assert.Equal(FindingOutcome.Pend, f.Outcome);
    }

    [Fact]
    public void Out_of_network_pend_can_be_disabled()
    {
        var rule = new ProviderEligibilityRule(new AdjudicationOptions { PendOutOfNetwork = false });
        Assert.Empty(rule.Evaluate(TestData.Context(provider: TestData.Provider(inNetwork: false))));
    }
}

public class TimelyFilingRuleTests
{
    [Theory]
    [InlineData(180, false)]
    [InlineData(181, true)]
    public void Lines_older_than_filing_limit_are_denied(int daysAgo, bool denied)
    {
        var member = TestData.Member(coverageStart: new DateOnly(2025, 1, 1));
        var claim = TestData.Claim(member, lines: [TestData.Line(daysAgo: daysAgo)]);

        var findings = new TimelyFilingRule().Evaluate(TestData.Context(claim, member)).ToList();

        Assert.Equal(denied, findings.Count == 1);
        if (denied) Assert.Equal(ReasonCodes.TimelyFilingExpired, findings[0].ReasonCode);
    }
}

public class ProcedureCoverageRuleTests
{
    [Fact]
    public void Code_not_on_fee_schedule_is_non_covered()
    {
        var claim = TestData.Claim(lines: [TestData.Line("99499")]);
        var f = Assert.Single(new ProcedureCoverageRule().Evaluate(TestData.Context(claim)));
        Assert.Equal(ReasonCodes.NonCoveredService, f.ReasonCode);
    }

    [Theory]
    [InlineData("ABCDE")]
    [InlineData("9921")]
    public void Malformed_code_is_invalid_info(string code)
    {
        var claim = TestData.Claim(lines: [TestData.Line(code)]);
        var f = Assert.Single(new ProcedureCoverageRule().Evaluate(TestData.Context(claim)));
        Assert.Equal(ReasonCodes.MissingOrInvalidInfo, f.ReasonCode);
    }

    [Theory]
    [InlineData("0001U")]
    [InlineData("J1100")]
    public void Recognizes_cpt_and_hcpcs_formats(string code) =>
        Assert.Matches(CodeFormats.ProcedureCode(), code);
}

public class PriorAuthorizationRuleTests
{
    [Fact]
    public void Missing_authorization_denies_line_with_carc_197()
    {
        var claim = TestData.Claim(lines: [TestData.Line("73721", billed: 1200m)]);
        var f = Assert.Single(new PriorAuthorizationRule().Evaluate(TestData.Context(claim)));
        Assert.Equal(ReasonCodes.PriorAuthAbsent, f.ReasonCode);
    }

    [Fact]
    public void Authorization_covering_service_date_passes()
    {
        var member = TestData.Member();
        var claim = TestData.Claim(member, lines: [TestData.Line("73721", daysAgo: 5, billed: 1200m)]);
        var auth = new PriorAuthorization(member.Id, "73721", "PA-1", TestData.Today.AddDays(-10), TestData.Today);

        Assert.Empty(new PriorAuthorizationRule().Evaluate(TestData.Context(claim, member, auths: [auth])));
    }

    [Fact]
    public void Expired_authorization_does_not_count()
    {
        var member = TestData.Member();
        var claim = TestData.Claim(member, lines: [TestData.Line("73721", daysAgo: 5, billed: 1200m)]);
        var auth = new PriorAuthorization(member.Id, "73721", "PA-1", TestData.Today.AddDays(-60), TestData.Today.AddDays(-30));

        Assert.Single(new PriorAuthorizationRule().Evaluate(TestData.Context(claim, member, auths: [auth])));
    }
}

public class DuplicateServiceRuleTests
{
    [Fact]
    public void Same_service_on_a_prior_claim_is_duplicate()
    {
        var claim = TestData.Claim(lines: [TestData.Line(daysAgo: 5)]);
        var history = new[] { new ExistingServiceLine("CLM-OLD", "99213", null, TestData.Today.AddDays(-5)) };

        var f = Assert.Single(new DuplicateServiceRule().Evaluate(TestData.Context(claim, history: history)));
        Assert.Equal(ReasonCodes.Duplicate, f.ReasonCode);
        Assert.Contains("CLM-OLD", f.Message);
    }

    [Fact]
    public void Different_modifier_or_date_is_not_duplicate()
    {
        var claim = TestData.Claim(lines: [TestData.Line(daysAgo: 5, modifier: "25")]);
        var history = new[]
        {
            new ExistingServiceLine("CLM-OLD", "99213", null, TestData.Today.AddDays(-5)),
            new ExistingServiceLine("CLM-OLD", "99213", "25", TestData.Today.AddDays(-6))
        };

        Assert.Empty(new DuplicateServiceRule().Evaluate(TestData.Context(claim, history: history)));
    }

    [Fact]
    public void Repeated_line_within_same_claim_is_duplicate()
    {
        var claim = TestData.Claim(lines: [TestData.Line(), TestData.Line()]);
        var f = Assert.Single(new DuplicateServiceRule().Evaluate(TestData.Context(claim)));
        Assert.Equal(2, f.LineNumber);
    }
}

public class HighDollarReviewRuleTests
{
    [Theory]
    [InlineData(9_999.99, false)]
    [InlineData(10_000, true)]
    public void Pends_at_or_above_threshold(decimal billed, bool pended)
    {
        var claim = TestData.Claim(lines: [TestData.Line("73721", billed: billed)]);
        var findings = new HighDollarReviewRule(new AdjudicationOptions()).Evaluate(TestData.Context(claim)).ToList();
        Assert.Equal(pended, findings.Any(f => f.Outcome == FindingOutcome.Pend));
    }
}
