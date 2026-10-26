using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Common;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Common;

namespace ClaimFlow.Application.Services;

public sealed class AdjudicationService(
    IClaimRepository claims,
    IMemberRepository members,
    IProviderRepository providers,
    IReferenceDataRepository referenceData,
    AdjudicationEngine engine,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const string SystemActor = "system:auto-adjudication";

    /// <summary>
    /// Auto-adjudicates a received claim. Idempotent: claims that are no longer <see cref="ClaimStatus.Received"/>
    /// are left untouched, so a message delivered twice by the queue is harmless.
    /// </summary>
    public async Task<ClaimStatus> AdjudicateAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await claims.GetAsync(claimId, ct) ?? throw new NotFoundException("Claim", claimId);
        if (claim.Status != ClaimStatus.Received) return claim.Status;

        var now = clock.UtcNow;
        claim.BeginAdjudication(now, SystemActor);

        var context = await BuildContextAsync(claim, ct);
        var evaluation = engine.Evaluate(context);
        claim.RecordFindings(evaluation.Findings.Select(f =>
            new AdjudicationFinding(claim.Id, f.RuleName, f.Outcome, f.ReasonCode, f.Message, f.LineNumber, now)));

        var decision = engine.Decide(context, evaluation);

        if (evaluation.HasPends && decision.AnyPayable)
        {
            claim.Pend(evaluation.PendSummary, now, SystemActor);
            audit.Record("Claim", claim.ClaimNumber, "Pended", SystemActor, evaluation.PendSummary);
        }
        else
        {
            claim.Finalize(decision.Lines, now, SystemActor, "Auto-adjudicated");
            context.Member.ApplyAccumulators(decision.PlanYear, decision.EndingAccumulators);
            audit.Record("Claim", claim.ClaimNumber, claim.Status.ToString(), SystemActor,
                $"Allowed={claim.TotalAllowed:0.00}; PlanPaid={claim.PlanPaid:0.00}; MemberResp={claim.MemberResponsibility:0.00}");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return claim.Status;
    }

    /// <summary>A reviewer releases or denies a pended claim. Rule denials still apply on approval.</summary>
    public async Task<ClaimStatus> ReviewAsync(Guid claimId, ReviewClaimCommand command, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Note))
            throw new RequestValidationException("note", "A review note is required.");

        var claim = await claims.GetAsync(claimId, ct) ?? throw new NotFoundException("Claim", claimId);
        if (claim.Status != ClaimStatus.Pended)
            throw new ConflictException($"Claim {claim.ClaimNumber} is {claim.Status}; only pended claims can be reviewed.");

        var now = clock.UtcNow;
        var context = await BuildContextAsync(claim, ct);

        AdjudicationDecision decision;
        if (command.Decision == ReviewDecision.Approve)
        {
            decision = engine.Decide(context, engine.Evaluate(context));
        }
        else
        {
            var code = string.IsNullOrWhiteSpace(command.DenialReasonCode) ? ReasonCodes.NotMedicallyNecessary : command.DenialReasonCode.Trim();
            decision = AdjudicationEngine.DenyAll(context, code);
        }

        claim.RecordFindings([new AdjudicationFinding(claim.Id, "ManualReview",
            command.Decision == ReviewDecision.Approve ? FindingOutcome.Pass : FindingOutcome.Deny,
            command.Decision == ReviewDecision.Deny ? decision.Lines[0].DenialCode : null,
            command.Note.Trim(), null, now)]);

        try
        {
            claim.Finalize(decision.Lines, now, actor, command.Note.Trim());
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        if (command.Decision == ReviewDecision.Approve)
            context.Member.ApplyAccumulators(decision.PlanYear, decision.EndingAccumulators);

        audit.Record("Claim", claim.ClaimNumber, $"Review{command.Decision}", actor, $"Status={claim.Status}");
        await unitOfWork.SaveChangesAsync(ct);
        return claim.Status;
    }

    private async Task<AdjudicationContext> BuildContextAsync(Claim claim, CancellationToken ct)
    {
        var member = await members.GetAsync(claim.MemberId, ct) ?? throw new NotFoundException("Member", claim.MemberId);
        var provider = await providers.GetAsync(claim.ProviderId, ct) ?? throw new NotFoundException("Provider", claim.ProviderId);
        var plan = member.Plan ?? throw new InvalidOperationException($"Plan not loaded for member {member.MemberNumber}.");

        var fees = await referenceData.GetFeeScheduleAsync(claim.Lines.Select(l => l.ProcedureCode).Distinct(), ct);
        var auths = await referenceData.GetPriorAuthorizationsAsync(member.Id, ct);
        var history = await claims.GetServiceHistoryAsync(member.Id, provider.Id, claim.ServiceDateFrom, claim.ServiceDateTo, claim.Id, ct);

        return new AdjudicationContext(claim, member, plan, provider, fees, auths, history);
    }
}
