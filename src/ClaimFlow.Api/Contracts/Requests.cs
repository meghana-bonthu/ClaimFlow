using System.Text.RegularExpressions;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;
using FluentValidation;

namespace ClaimFlow.Api.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Email, string DisplayName, string Role);

public sealed record SubmitClaimRequest(
    string MemberNumber,
    string ProviderNpi,
    string? PatientControlNumber,
    List<string> DiagnosisCodes,
    List<SubmitClaimLineRequest> Lines)
{
    public SubmitClaimCommand ToCommand() => new(
        MemberNumber,
        ProviderNpi,
        PatientControlNumber,
        DiagnosisCodes,
        Lines.Select(l => new SubmitClaimLine(l.ProcedureCode, l.Modifier, l.ServiceDate, l.Units, l.BilledAmount)).ToList());
}

public sealed record SubmitClaimLineRequest(string ProcedureCode, string? Modifier, DateOnly ServiceDate, int Units, decimal BilledAmount);

public sealed record ReviewClaimRequest(ReviewDecision Decision, string Note, string? DenialReasonCode)
{
    public ReviewClaimCommand ToCommand() => new(Decision, Note, DenialReasonCode);
}

public sealed partial class SubmitClaimRequestValidator : AbstractValidator<SubmitClaimRequest>
{
    public SubmitClaimRequestValidator(TimeProvider time)
    {
        RuleFor(x => x.MemberNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ProviderNpi).NotEmpty().Matches(Npi()).WithMessage("NPI must be exactly 10 digits.");
        RuleFor(x => x.PatientControlNumber).MaximumLength(38);

        RuleFor(x => x.DiagnosisCodes).NotEmpty().Must(d => d.Count <= Claim.MaxDiagnosisCodes)
            .WithMessage($"No more than {Claim.MaxDiagnosisCodes} diagnosis codes are allowed.");
        // Loose shape check only; strict ICD-10-CM validation is an adjudication rule so it is audited as a finding.
        RuleForEach(x => x.DiagnosisCodes).NotEmpty().Matches(DiagnosisShape()).WithMessage("'{PropertyValue}' is not a diagnosis code.");

        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= Claim.MaxLines)
            .WithMessage($"No more than {Claim.MaxLines} service lines are allowed.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProcedureCode).NotEmpty().Matches(ProcedureShape()).WithMessage("Procedure code must be 5 letters/digits.");
            line.RuleFor(l => l.Modifier).Length(2).When(l => !string.IsNullOrEmpty(l.Modifier));
            line.RuleFor(l => l.Units).InclusiveBetween(1, 999);
            line.RuleFor(l => l.BilledAmount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
            line.RuleFor(l => l.ServiceDate)
                .LessThanOrEqualTo(_ => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)).WithMessage("Service date cannot be in the future.")
                .GreaterThan(new DateOnly(2000, 1, 1));
        });
    }

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex Npi();

    [GeneratedRegex(@"^[A-Za-z0-9.]{3,8}$")]
    private static partial Regex DiagnosisShape();

    [GeneratedRegex(@"^[A-Za-z0-9]{5}$")]
    private static partial Regex ProcedureShape();
}

public sealed class ReviewClaimRequestValidator : AbstractValidator<ReviewClaimRequest>
{
    public ReviewClaimRequestValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DenialReasonCode).MaximumLength(10);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
