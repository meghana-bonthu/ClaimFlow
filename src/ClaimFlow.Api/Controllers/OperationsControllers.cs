using ClaimFlow.Api.Auth;
using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(ClaimQueryService queries) : ControllerBase
{
    /// <summary>Volume, dollars, auto-adjudication rate, turnaround and top denial reasons.</summary>
    [HttpGet]
    public Task<DashboardSummary> Get(CancellationToken ct) => queries.GetDashboardAsync(ct);
}

[ApiController]
[Authorize(Policy = Policies.ReleasePayments)]
[Route("api/payments")]
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>Run an EFT payment cycle for all approved, unpaid claims.</summary>
    [HttpPost("release")]
    public Task<PaymentRunDto> Release(CancellationToken ct) => payments.ReleasePaymentsAsync(User.Actor(), ct);
}

[ApiController]
[Authorize]
[Route("api")]
public sealed class ReferenceDataController(
    IMemberRepository members,
    IProviderRepository providers,
    IReferenceDataRepository referenceData) : ControllerBase
{
    [HttpGet("members")]
    public Task<IReadOnlyList<MemberDto>> Members([FromQuery] string? search, CancellationToken ct) => members.SearchAsync(search, ct);

    [HttpGet("providers")]
    public Task<IReadOnlyList<ProviderDto>> Providers([FromQuery] string? search, CancellationToken ct) => providers.SearchAsync(search, ct);

    [HttpGet("fee-schedule")]
    public Task<IReadOnlyList<FeeScheduleDto>> FeeSchedule(CancellationToken ct) => referenceData.ListFeeScheduleAsync(ct);
}
