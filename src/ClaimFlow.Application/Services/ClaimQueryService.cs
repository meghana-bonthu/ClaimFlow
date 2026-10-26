using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Common;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Services;

public sealed class ClaimQueryService(
    IClaimRepository claims,
    IMemberRepository members,
    IProviderRepository providers,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<ClaimDetailDto> GetDetailAsync(Guid id, string actor, CancellationToken ct)
    {
        var claim = await claims.GetAsync(id, ct) ?? throw new NotFoundException("Claim", id);
        var member = await members.GetAsync(claim.MemberId, ct) ?? throw new NotFoundException("Member", claim.MemberId);
        var provider = await providers.GetAsync(claim.ProviderId, ct) ?? throw new NotFoundException("Provider", claim.ProviderId);

        // HIPAA: record every view of a claim (which contains PHI).
        audit.Record("Claim", claim.ClaimNumber, "Viewed", actor);
        await unitOfWork.SaveChangesAsync(ct);

        return new ClaimDetailDto(
            claim.Id,
            claim.ClaimNumber,
            claim.PatientControlNumber,
            claim.Status,
            claim.Source,
            claim.PendReason,
            claim.SubmittedAt,
            claim.SubmittedBy,
            claim.AdjudicatedAt,
            claim.PaidAt,
            claim.PaymentReference,
            new MemberSummaryDto(member.Id, member.MemberNumber, member.FullName, member.Plan?.Name ?? string.Empty),
            new ProviderDto(provider.Id, provider.Npi, provider.Name, provider.Specialty, provider.IsInNetwork, provider.IsActive),
            claim.DiagnosisCodes,
            claim.TotalBilled,
            claim.TotalAllowed,
            claim.PlanPaid,
            claim.MemberResponsibility,
            claim.Lines.OrderBy(l => l.LineNumber).Select(l => new ClaimLineDto(
                l.LineNumber, l.ProcedureCode, l.Modifier, l.ServiceDate, l.Units, l.BilledAmount, l.Status,
                l.AllowedAmount, l.DeductibleAmount, l.CopayAmount, l.CoinsuranceAmount, l.PlanPaid,
                l.DenialCode, l.DenialCode is null ? null : ReasonCodes.Describe(l.DenialCode))).ToList(),
            claim.Findings.OrderBy(f => f.Sequence).Select(f => new FindingDto(f.RuleName, f.Outcome, f.ReasonCode, f.Message, f.LineNumber)).ToList(),
            claim.History.OrderBy(h => h.Sequence).Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedAt, h.Actor, h.Note)).ToList());
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditTrailAsync(Guid id, CancellationToken ct)
    {
        var claim = await claims.GetAsync(id, ct) ?? throw new NotFoundException("Claim", id);
        return await audit.GetForEntityAsync("Claim", claim.ClaimNumber, ct);
    }

    public Task<PagedResult<ClaimListItem>> SearchAsync(ClaimSearchCriteria criteria, CancellationToken ct)
    {
        var safe = criteria with
        {
            Page = Math.Max(1, criteria.Page),
            PageSize = Math.Clamp(criteria.PageSize, 1, 100)
        };
        return claims.SearchAsync(safe, ct);
    }

    public Task<DashboardSummary> GetDashboardAsync(CancellationToken ct) =>
        claims.GetDashboardAsync(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct);
}
