using System.Net;
using System.Net.Http.Json;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ClaimsApiTests(ClaimFlowApiFactory factory)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ClaimFlowApiFactory.Json;
    private static DateOnly DaysAgo(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);

    private static SubmitClaimRequest Request(string member, string npi, string pcn, params SubmitClaimLineRequest[] lines) =>
        new(member, npi, pcn, ["J06.9"], lines.ToList());

    private static SubmitClaimLineRequest Line(string code, decimal billed, int daysAgo = 3, int units = 1) =>
        new(code, null, DaysAgo(daysAgo), units, billed);

    private static async Task<ClaimDetailDto> WaitForStatusAsync(HttpClient client, Guid id, params ClaimStatus[] expected)
    {
        for (var i = 0; i < 50; i++)
        {
            var claim = await client.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{id}", Json);
            if (expected.Contains(claim!.Status)) return claim;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Claim {id} never reached {string.Join("/", expected)}.");
    }

    private static async Task<ClaimSubmittedDto> SubmitAsync(HttpClient client, SubmitClaimRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/claims", request, Json);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClaimSubmittedDto>(Json))!;
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous_and_healthy()
    {
        var response = await factory.CreateClient().GetStringAsync("/health");
        Assert.Equal("Healthy", response);
    }

    [Fact]
    public async Task Claims_require_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@claimflow.dev", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submitted_claim_is_auto_adjudicated_and_priced()
    {
        var client = await factory.SubmitterAsync();
        // M100003 is on Silver HMO ($40 copay); a fresh member for this test keeps accumulators predictable.
        var submitted = await SubmitAsync(client, Request("M100003", "1234567893", "IT-PRICE-1", Line("99213", 150m)));

        Assert.Equal(ClaimStatus.Received, submitted.Status);
        Assert.StartsWith("CLM-", submitted.ClaimNumber);

        var claim = await WaitForStatusAsync(client, submitted.Id, ClaimStatus.Approved);
        var line = Assert.Single(claim.Lines);
        Assert.Equal(92m, line.AllowedAmount);
        Assert.Equal(40m, line.CopayAmount);
        Assert.Equal(52m, line.PlanPaid);
        Assert.Equal(["J069"], claim.DiagnosisCodes);
        Assert.Equal(8, claim.Findings.Count);
        Assert.All(claim.Findings, f => Assert.Equal(FindingOutcome.Pass, f.Outcome));
        Assert.Equal([ClaimStatus.Received, ClaimStatus.InAdjudication, ClaimStatus.Approved], claim.History.Select(h => h.To));
    }

    [Fact]
    public async Task Terminated_member_claim_is_denied_with_carc_27()
    {
        var client = await factory.SubmitterAsync();
        var submitted = await SubmitAsync(client, Request("M100006", "1234567893", "IT-TERM-1", Line("99213", 150m, daysAgo: 5)));

        var claim = await WaitForStatusAsync(client, submitted.Id, ClaimStatus.Denied);
        Assert.Equal("27", Assert.Single(claim.Lines).DenialCode);
        Assert.Equal(0m, claim.PlanPaid);
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem()
    {
        var client = await factory.SubmitterAsync();
        var response = await client.PostAsJsonAsync("/api/claims", new SubmitClaimRequest("", "123", null, [], []), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains("ProviderNpi", problem!.Errors.Keys);
        Assert.Contains("Lines", problem.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_member_returns_validation_problem()
    {
        var client = await factory.SubmitterAsync();
        var response = await client.PostAsJsonAsync("/api/claims", Request("M000000", "1234567893", "IT-UNK", Line("99213", 100m)), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("memberNumber", (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!.Errors.Keys);
    }

    [Fact]
    public async Task Resubmitting_same_patient_control_number_returns_409()
    {
        var client = await factory.SubmitterAsync();
        var request = Request("M100004", "1567890128", "IT-DUP-PCN", Line("97110", 60m));
        await SubmitAsync(client, request);

        var second = await client.PostAsJsonAsync("/api/claims", request, Json);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Out_of_network_claim_pends_and_adjudicator_can_approve_it()
    {
        var submitter = await factory.SubmitterAsync();
        var submitted = await SubmitAsync(submitter, Request("M100001", "1982736450", "IT-OON-1", Line("93000", 95m)));
        await WaitForStatusAsync(submitter, submitted.Id, ClaimStatus.Pended);

        // Submitters cannot review.
        var forbidden = await submitter.PostAsJsonAsync($"/api/claims/{submitted.Id}/review",
            new ReviewClaimRequest(ReviewDecision.Approve, "ok", null), Json);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var adjudicator = await factory.AdjudicatorAsync();
        var response = await adjudicator.PostAsJsonAsync($"/api/claims/{submitted.Id}/review",
            new ReviewClaimRequest(ReviewDecision.Approve, "Continuity of care exception", null), Json);
        response.EnsureSuccessStatusCode();

        var claim = await adjudicator.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{submitted.Id}", Json);
        Assert.Equal(ClaimStatus.Approved, claim!.Status);
        Assert.Equal("ManualReview", claim.Findings[^1].RuleName);
        Assert.Equal("adjudicator@claimflow.dev", claim.History[^1].Actor);

        // Reviewing again is a state conflict.
        var again = await adjudicator.PostAsJsonAsync($"/api/claims/{submitted.Id}/review",
            new ReviewClaimRequest(ReviewDecision.Deny, "changed mind", null), Json);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task X12_upload_accepts_valid_claims_and_rejects_bad_ones()
    {
        var client = await factory.SubmitterAsync();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync("sample-837p.txt")), "file", "sample-837p.txt" }
        };

        var response = await client.PostAsync("/api/claims/x12", content);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<X12BatchResultDto>(Json);

        Assert.Equal("000000417", result!.InterchangeControlNumber);
        Assert.Equal(4, result.ClaimsInFile);
        Assert.Equal(2, result.Accepted.Count);
        Assert.Equal(2, result.Rejected.Count);
        Assert.Contains(result.Rejected, r => r.Errors.Any(e => e.Contains("M999999")));
        Assert.Contains(result.Rejected, r => r.Errors.Any(e => e.Contains("CLM02")));

        foreach (var accepted in result.Accepted)
        {
            var claim = await WaitForStatusAsync(client, accepted.Id, ClaimStatus.Approved, ClaimStatus.Denied, ClaimStatus.Pended);
            Assert.Equal(ClaimSource.X12Edi, claim.Source);
        }
    }

    [Fact]
    public async Task Payment_run_pays_approved_claims_and_remittance_is_downloadable()
    {
        var submitter = await factory.SubmitterAsync();
        var submitted = await SubmitAsync(submitter, Request("M100008", "1234567893", "IT-PAY-1", Line("99214", 210m)));
        await WaitForStatusAsync(submitter, submitted.Id, ClaimStatus.Approved);

        Assert.Equal(HttpStatusCode.Forbidden, (await submitter.PostAsync("/api/payments/release", null)).StatusCode);

        var admin = await factory.AdminAsync();
        var run = await (await admin.PostAsync("/api/payments/release", null)).Content.ReadFromJsonAsync<PaymentRunDto>(Json);
        Assert.Contains(submitted.ClaimNumber, run!.ClaimNumbers);

        var claim = await admin.GetFromJsonAsync<ClaimDetailDto>($"/api/claims/{submitted.Id}", Json);
        Assert.Equal(ClaimStatus.Paid, claim!.Status);
        Assert.StartsWith(run.RunReference, claim.PaymentReference);

        var remittance = await admin.GetStringAsync($"/api/claims/{submitted.Id}/remittance");
        Assert.StartsWith("ISA*", remittance);
        Assert.Contains($"CLP*IT-PAY-1*1*210*", remittance);
        Assert.Contains($"TRN*1*{claim.PaymentReference}", remittance);

        var audit = await admin.GetFromJsonAsync<List<AuditEntryDto>>($"/api/claims/{submitted.Id}/audit", Json);
        Assert.Contains(audit!, a => a.Action == "Submitted" && a.Actor == "submitter@claimflow.dev");
        Assert.Contains(audit!, a => a.Action == "Viewed");
        Assert.Contains(audit!, a => a.Action == "RemittanceDownloaded");
    }

    [Fact]
    public async Task Search_and_dashboard_reflect_submitted_claims()
    {
        var client = await factory.AdminAsync();
        var submitted = await SubmitAsync(client, Request("M100002", "1567890128", "IT-SEARCH-1", Line("97140", 55m)));
        await WaitForStatusAsync(client, submitted.Id, ClaimStatus.Approved);

        var page = await client.GetFromJsonAsync<PagedResult<ClaimListItem>>("/api/claims?search=it-search-1", Json);
        var item = Assert.Single(page!.Items);
        Assert.Equal("John Smith", item.MemberName);

        var dashboard = await client.GetFromJsonAsync<DashboardSummary>("/api/dashboard", Json);
        Assert.True(dashboard!.TotalClaims >= 1);
        Assert.Equal(14, dashboard.DailyVolume.Count);
        Assert.Equal(Enum.GetValues<ClaimStatus>().Length, dashboard.ByStatus.Count);
    }

    [Fact]
    public async Task Reference_data_endpoints_return_seeded_data()
    {
        var client = await factory.SubmitterAsync();
        var members = await client.GetFromJsonAsync<List<MemberDto>>("/api/members?search=garcia", Json);
        var providers = await client.GetFromJsonAsync<List<ProviderDto>>("/api/providers", Json);
        var fees = await client.GetFromJsonAsync<List<FeeScheduleDto>>("/api/fee-schedule", Json);

        Assert.Equal("M100003", Assert.Single(members!).MemberNumber);
        Assert.Equal(6, providers!.Count);
        Assert.Contains(fees!, f => f.ProcedureCode == "73721" && f.RequiresPriorAuthorization);
    }
}
