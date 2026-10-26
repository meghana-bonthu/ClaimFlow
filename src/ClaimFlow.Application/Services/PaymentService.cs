using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Common;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Application.X12;

namespace ClaimFlow.Application.Services;

public sealed class PaymentService(
    IClaimRepository claims,
    IMemberRepository members,
    IProviderRepository providers,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const int MaxClaimsPerRun = 500;

    /// <summary>Pays every approved claim in one EFT run; each claim gets a unique trace number.</summary>
    public async Task<PaymentRunDto> ReleasePaymentsAsync(string actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var runReference = $"EFT{now:yyyyMMddHHmmss}";
        var approved = await claims.GetApprovedUnpaidAsync(MaxClaimsPerRun, ct);

        var seq = 1;
        foreach (var claim in approved)
            claim.MarkPaid($"{runReference}-{seq++:D4}", now, actor);

        var total = approved.Sum(c => c.PlanPaid);
        audit.Record("PaymentRun", runReference, "Released", actor, $"Claims={approved.Count}; Total={total:0.00}");
        await unitOfWork.SaveChangesAsync(ct);

        return new PaymentRunDto(runReference, approved.Count, total, approved.Select(c => c.ClaimNumber).ToList());
    }

    public async Task<string> GetRemittanceAsync(Guid claimId, string actor, CancellationToken ct)
    {
        var claim = await claims.GetAsync(claimId, ct) ?? throw new NotFoundException("Claim", claimId);
        var member = await members.GetAsync(claim.MemberId, ct) ?? throw new NotFoundException("Member", claim.MemberId);
        var provider = await providers.GetAsync(claim.ProviderId, ct) ?? throw new NotFoundException("Provider", claim.ProviderId);

        string remittance;
        try
        {
            remittance = X12835Builder.Build(claim, member, provider, clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        // Downloading an 835 discloses PHI, so it is audited like any other access.
        audit.Record("Claim", claim.ClaimNumber, "RemittanceDownloaded", actor);
        await unitOfWork.SaveChangesAsync(ct);
        return remittance;
    }
}
