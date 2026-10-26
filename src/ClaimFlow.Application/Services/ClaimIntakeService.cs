using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Common;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Application.X12;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Common;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.Application.Services;

/// <summary>
/// Accepts claims from the API, the portal and X12 837 files. Intake only checks that the claim is
/// well-formed and references known entities; business rules run later in the adjudication worker.
/// </summary>
public sealed class ClaimIntakeService(
    IClaimRepository claims,
    IMemberRepository members,
    IProviderRepository providers,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClaimQueue queue,
    IClock clock,
    IClaimNumberGenerator numbers)
{
    public async Task<ClaimSubmittedDto> SubmitAsync(SubmitClaimCommand command, ClaimSource source, string actor, CancellationToken ct)
    {
        var claim = await CreateClaimAsync(command, source, actor, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await queue.EnqueueAsync(claim.Id, ct);
        return new ClaimSubmittedDto(claim.Id, claim.ClaimNumber, claim.Status);
    }

    public async Task<X12BatchResultDto> SubmitX12Async(string fileContent, string actor, CancellationToken ct)
    {
        var parsed = X12837Parser.Parse(fileContent);
        var accepted = new List<Claim>();
        var rejected = new List<X12RejectedClaimDto>();

        for (var i = 0; i < parsed.Claims.Count; i++)
        {
            var pc = parsed.Claims[i];
            if (pc.Errors.Count > 0)
            {
                rejected.Add(new X12RejectedClaimDto(i + 1, pc.PatientControlNumber, pc.Errors));
                continue;
            }

            var command = new SubmitClaimCommand(
                pc.MemberNumber!,
                pc.BillingProviderNpi!,
                pc.PatientControlNumber,
                pc.DiagnosisCodes,
                pc.Lines.Select(l => new SubmitClaimLine(l.ProcedureCode, l.Modifier, l.ServiceDate!.Value, l.Units, l.Charge)).ToList());

            try
            {
                accepted.Add(await CreateClaimAsync(command, ClaimSource.X12Edi, actor, ct, accepted));
            }
            catch (RequestValidationException ex)
            {
                rejected.Add(new X12RejectedClaimDto(i + 1, pc.PatientControlNumber, ex.Errors.SelectMany(e => e.Value).ToList()));
            }
            catch (ConflictException ex)
            {
                rejected.Add(new X12RejectedClaimDto(i + 1, pc.PatientControlNumber, [ex.Message]));
            }
        }

        audit.Record("X12File", parsed.InterchangeControlNumber ?? "unknown", "Imported", actor,
            $"Claims={parsed.Claims.Count}; Accepted={accepted.Count}; Rejected={rejected.Count}");

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var claim in accepted)
            await queue.EnqueueAsync(claim.Id, ct);

        return new X12BatchResultDto(
            parsed.InterchangeControlNumber,
            parsed.Claims.Count,
            accepted.Select(c => new ClaimSubmittedDto(c.Id, c.ClaimNumber, c.Status)).ToList(),
            rejected,
            parsed.Errors);
    }

    private async Task<Claim> CreateClaimAsync(SubmitClaimCommand command, ClaimSource source, string actor,
        CancellationToken ct, IReadOnlyCollection<Claim>? pendingInBatch = null)
    {
        var errors = new Dictionary<string, string[]>();

        if (!NpiValidator.IsValid(command.ProviderNpi))
            errors["providerNpi"] = [$"'{command.ProviderNpi}' is not a valid NPI."];

        var member = await members.GetByNumberAsync(command.MemberNumber, ct);
        if (member is null) errors["memberNumber"] = [$"Member '{command.MemberNumber}' was not found."];

        var provider = errors.ContainsKey("providerNpi") ? null : await providers.GetByNpiAsync(command.ProviderNpi, ct);
        if (provider is null && !errors.ContainsKey("providerNpi"))
            errors["providerNpi"] = [$"Provider with NPI '{command.ProviderNpi}' was not found."];

        if (errors.Count > 0) throw new RequestValidationException(errors);

        var pcn = command.PatientControlNumber?.Trim();
        if (!string.IsNullOrEmpty(pcn))
        {
            var dupInBatch = pendingInBatch?.Any(c => c.ProviderId == provider!.Id && c.PatientControlNumber == pcn) ?? false;
            if (dupInBatch || await claims.PatientControlNumberExistsAsync(provider!.Id, pcn, ct))
                throw new ConflictException($"Patient control number '{pcn}' was already submitted by this provider.");
        }

        Claim claim;
        try
        {
            claim = Claim.Create(
                numbers.Next(),
                member!.Id,
                provider!.Id,
                source,
                command.DiagnosisCodes,
                command.Lines.Select(l => new NewClaimLine(l.ProcedureCode, l.ServiceDate, l.Units, l.BilledAmount, l.Modifier)),
                clock.UtcNow,
                actor,
                pcn);
        }
        catch (DomainException ex)
        {
            throw new RequestValidationException("claim", ex.Message);
        }

        claims.Add(claim);
        audit.Record("Claim", claim.ClaimNumber, "Submitted", actor, $"Source={source}; Lines={claim.Lines.Count}; Billed={claim.TotalBilled:0.00}");
        return claim;
    }
}
