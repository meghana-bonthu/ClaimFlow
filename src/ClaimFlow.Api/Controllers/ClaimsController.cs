using System.Text;
using ClaimFlow.Api.Auth;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Application.Services;
using ClaimFlow.Domain.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/claims")]
public sealed class ClaimsController(
    ClaimIntakeService intake,
    ClaimQueryService queries,
    AdjudicationService adjudication,
    PaymentService payments) : ControllerBase
{
    public const long MaxX12FileBytes = 5 * 1024 * 1024;

    /// <summary>Search claims by status and free text (claim #, member #, last name, NPI, patient control #).</summary>
    [HttpGet]
    public Task<PagedResult<ClaimListItem>> Search([FromQuery] ClaimStatus? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        queries.SearchAsync(new ClaimSearchCriteria(status, search, page, pageSize), ct);

    /// <summary>Full claim detail including line pricing, rule findings and status history. Access is audited.</summary>
    [HttpGet("{id:guid}")]
    public Task<ClaimDetailDto> Get(Guid id, CancellationToken ct) => queries.GetDetailAsync(id, User.Actor(), ct);

    /// <summary>Submit a professional claim. It is queued for auto-adjudication immediately.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.SubmitClaims)]
    [ProducesResponseType<ClaimSubmittedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(SubmitClaimRequest request, [FromServices] IValidator<SubmitClaimRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var result = await intake.SubmitAsync(request.ToCommand(), ClaimSource.Portal, User.Actor(), ct);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>Upload an X12 837P file (multipart field "file"). Each claim in the file is accepted or rejected independently.</summary>
    [HttpPost("x12")]
    [Authorize(Policy = Policies.SubmitClaims)]
    [RequestSizeLimit(MaxX12FileBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<X12BatchResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadX12(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxX12FileBytes)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["File must be between 1 byte and 5 MB."] }));

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync(ct);
        var result = await intake.SubmitX12Async(content, User.Actor(), ct);
        return Ok(result);
    }

    /// <summary>Approve or deny a pended claim.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = Policies.ReviewClaims)]
    public async Task<IActionResult> Review(Guid id, ReviewClaimRequest request, [FromServices] IValidator<ReviewClaimRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var status = await adjudication.ReviewAsync(id, request.ToCommand(), User.Actor(), ct);
        return Ok(new { id, status });
    }

    /// <summary>Download the X12 835 remittance advice for an adjudicated claim.</summary>
    [HttpGet("{id:guid}/remittance")]
    [Produces("text/plain")]
    public async Task<IActionResult> Remittance(Guid id, CancellationToken ct)
    {
        var content = await payments.GetRemittanceAsync(id, User.Actor(), ct);
        return File(Encoding.ASCII.GetBytes(content), "text/plain", $"835-{id:N}.txt");
    }

    /// <summary>HIPAA audit trail for a claim (who submitted, viewed, reviewed and paid it).</summary>
    [HttpGet("{id:guid}/audit")]
    [Authorize(Policy = Policies.ViewAudit)]
    public Task<IReadOnlyList<AuditEntryDto>> Audit(Guid id, CancellationToken ct) => queries.GetAuditTrailAsync(id, ct);
}
