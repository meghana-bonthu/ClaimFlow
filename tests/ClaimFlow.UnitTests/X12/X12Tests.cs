using ClaimFlow.Application.X12;
using ClaimFlow.Domain.Claims;

namespace ClaimFlow.UnitTests.X12;

public class X12837ParserTests
{
    private const string Isa = "ISA*00*          *00*          *ZZ*SENDER         *ZZ*CLAIMFLOW      *261021*0930*^*00501*000000417*0*T*:~";

    private static string File(string body) =>
        Isa + "\nGS*HC*SENDER*CLAIMFLOW*20261021*0930*1*X*005010X222A1~\nST*837*0001*005010X222A1~\n" + body + "\nSE*10*0001~\nGE*1*1~\nIEA*1*000000417~";

    private const string ProviderLoop = "NM1*85*2*RIVERSIDE FAMILY MEDICINE*****XX*1234567893~\n";

    [Fact]
    public void Parses_provider_subscriber_claim_diagnoses_and_lines()
    {
        var result = X12837Parser.Parse(File(ProviderLoop + """
            NM1*IL*1*DOE*JANE****MI*M100001~
            CLM*PCN-1*175***11:B:1*Y*A*Y*Y~
            HI*ABK:J029*ABF:R509~
            LX*1~
            SV1*HC:99213:25*140*UN*1***1~
            DTP*472*D8*20261015~
            LX*2~
            SV1*HC:87880*35*UN*1***1~
            DTP*472*D8*20261015~
            """));

        Assert.Empty(result.Errors);
        Assert.Equal("000000417", result.InterchangeControlNumber);
        var claim = Assert.Single(result.Claims);
        Assert.Empty(claim.Errors);
        Assert.Equal("1234567893", claim.BillingProviderNpi);
        Assert.Equal("M100001", claim.MemberNumber);
        Assert.Equal("PCN-1", claim.PatientControlNumber);
        Assert.Equal(175m, claim.TotalCharge);
        Assert.Equal(["J029", "R509"], claim.DiagnosisCodes);
        Assert.Equal(2, claim.Lines.Count);
        Assert.Equal(new ParsedServiceLine("99213", "25", 140m, 1, new DateOnly(2026, 10, 15)), claim.Lines[0]);
    }

    [Fact]
    public void Claim_level_service_date_applies_to_lines_without_their_own()
    {
        var result = X12837Parser.Parse(File(ProviderLoop + """
            NM1*IL*1*DOE*JANE****MI*M100001~
            CLM*PCN-1*35***11:B:1*Y*A*Y*Y~
            DTP*472*D8*20261001~
            HI*ABK:J029~
            LX*1~
            SV1*HC:87880*35*UN*1***1~
            """));

        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(Assert.Single(result.Claims).Lines).ServiceDate);
    }

    [Fact]
    public void Multiple_subscribers_produce_multiple_claims()
    {
        var result = X12837Parser.Parse(File(ProviderLoop + """
            NM1*IL*1*DOE*JANE****MI*M100001~
            CLM*A*35***11:B:1*Y*A*Y*Y~
            HI*ABK:J029~
            LX*1~
            SV1*HC:87880*35*UN*1***1~
            DTP*472*D8*20261001~
            NM1*IL*1*ROE*RICH****MI*M100002~
            CLM*B*92***11:B:1*Y*A*Y*Y~
            HI*ABK:Z0000~
            LX*1~
            SV1*HC:99213*92*UN*1***1~
            DTP*472*RD8*20261002-20261003~
            """));

        Assert.Equal(2, result.Claims.Count);
        Assert.Equal("M100002", result.Claims[1].MemberNumber);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Claims[1].Lines[0].ServiceDate);
    }

    [Fact]
    public void Total_charge_mismatch_is_reported_on_the_claim()
    {
        var result = X12837Parser.Parse(File(ProviderLoop + """
            NM1*IL*1*DOE*JANE****MI*M100001~
            CLM*PCN-1*999***11:B:1*Y*A*Y*Y~
            HI*ABK:J029~
            LX*1~
            SV1*HC:87880*35*UN*1***1~
            DTP*472*D8*20261015~
            """));

        Assert.Contains(Assert.Single(result.Claims).Errors, e => e.Contains("CLM02"));
    }

    [Fact]
    public void Uses_delimiters_declared_in_isa_header()
    {
        var content = Isa.Replace('*', '|').Replace("|:~", "|>!") + "ST|837|0001!" +
                      "NM1|85|2|CLINIC|||||XX|1234567893!NM1|IL|1|DOE|JANE||||MI|M100001!" +
                      "CLM|P1|35|||11>B>1!HI|ABK>J029!LX|1!SV1|HC>87880|35|UN|1!DTP|472|D8|20261015!SE|8|0001!";

        var claim = Assert.Single(X12837Parser.Parse(content).Claims);
        Assert.Empty(claim.Errors);
        Assert.Equal("87880", claim.Lines[0].ProcedureCode);
        Assert.Equal(["J029"], claim.DiagnosisCodes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("GS*HC*X~ST*837~")]
    public void Rejects_files_without_isa_header(string content)
    {
        var result = X12837Parser.Parse(content);
        Assert.Empty(result.Claims);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Reports_missing_required_loops()
    {
        var claim = Assert.Single(X12837Parser.Parse(File("CLM*PCN-1*35***11:B:1*Y*A*Y*Y~")).Claims);
        Assert.Contains(claim.Errors, e => e.Contains("NPI"));
        Assert.Contains(claim.Errors, e => e.Contains("member ID"));
        Assert.Contains(claim.Errors, e => e.Contains("diagnosis"));
        Assert.Contains(claim.Errors, e => e.Contains("service lines"));
    }
}

public class X12835BuilderTests
{
    [Fact]
    public void Builds_remittance_with_adjustment_segments()
    {
        var member = TestData.Member();
        var provider = TestData.Provider();
        var claim = TestData.Claim(member, provider, lines: [TestData.Line(billed: 150m), TestData.Line("80053", billed: 40m)]).InAdjudication();
        claim.Finalize(
        [
            LineDecision.Approve(1, new LinePricing(92m, 0m, 25m, 0m, 67m)),
            LineDecision.Deny(2, ReasonCodes.NonCoveredService)
        ], TestData.Now, "test");

        var edi = X12835Builder.Build(claim, member, provider, TestData.Now);
        var segments = edi.Split("~\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("ISA*", segments[0]);
        Assert.Equal(106, segments[0].Length + 1); // fixed-width ISA including terminator
        Assert.Contains("BPR*I*67*C*ACH************20261020", segments);
        Assert.Contains("CLP*CLM-TEST-000001*1*190*67*25*12*CLM-TEST-000001", segments);
        Assert.Contains("CAS*CO*45*58", segments); // contractual write-off 150 - 92
        Assert.Contains("CAS*PR*3*25", segments);  // copay
        Assert.Contains("CAS*CO*96*40", segments); // denied line

        var st = Array.FindIndex(segments, s => s.StartsWith("ST*"));
        var se = Array.FindIndex(segments, s => s.StartsWith("SE*"));
        Assert.Equal($"SE*{se - st + 1}*0001", segments[se]);
    }

    [Fact]
    public void Refuses_unadjudicated_claims()
    {
        var claim = TestData.Claim();
        Assert.Throws<InvalidOperationException>(() => X12835Builder.Build(claim, TestData.Member(), TestData.Provider(), TestData.Now));
    }
}
