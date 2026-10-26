using ClaimFlow.Domain.Claims;

namespace ClaimFlow.Application.Contracts;

// ---------- Commands ----------

public sealed record SubmitClaimCommand(
    string MemberNumber,
    string ProviderNpi,
    string? PatientControlNumber,
    IReadOnlyList<string> DiagnosisCodes,
    IReadOnlyList<SubmitClaimLine> Lines);

public sealed record SubmitClaimLine(string ProcedureCode, string? Modifier, DateOnly ServiceDate, int Units, decimal BilledAmount);

public enum ReviewDecision
{
    Approve,
    Deny
}

public sealed record ReviewClaimCommand(ReviewDecision Decision, string Note, string? DenialReasonCode);

// ---------- Results ----------

public sealed record ClaimSubmittedDto(Guid Id, string ClaimNumber, ClaimStatus Status);

public sealed record X12BatchResultDto(
    string? InterchangeControlNumber,
    int ClaimsInFile,
    IReadOnlyList<ClaimSubmittedDto> Accepted,
    IReadOnlyList<X12RejectedClaimDto> Rejected,
    IReadOnlyList<string> FileErrors);

public sealed record X12RejectedClaimDto(int Index, string? PatientControlNumber, IReadOnlyList<string> Errors);

public sealed record PaymentRunDto(string RunReference, int ClaimsPaid, decimal TotalPaid, IReadOnlyList<string> ClaimNumbers);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ClaimSearchCriteria(ClaimStatus? Status = null, string? Search = null, int Page = 1, int PageSize = 20);

public sealed record ClaimListItem(
    Guid Id,
    string ClaimNumber,
    string MemberNumber,
    string MemberName,
    string ProviderName,
    DateOnly ServiceDateFrom,
    ClaimStatus Status,
    ClaimSource Source,
    decimal TotalBilled,
    decimal PlanPaid,
    DateTimeOffset SubmittedAt);

public sealed record ClaimDetailDto(
    Guid Id,
    string ClaimNumber,
    string? PatientControlNumber,
    ClaimStatus Status,
    ClaimSource Source,
    string? PendReason,
    DateTimeOffset SubmittedAt,
    string SubmittedBy,
    DateTimeOffset? AdjudicatedAt,
    DateTimeOffset? PaidAt,
    string? PaymentReference,
    MemberSummaryDto Member,
    ProviderDto Provider,
    IReadOnlyList<string> DiagnosisCodes,
    decimal TotalBilled,
    decimal TotalAllowed,
    decimal PlanPaid,
    decimal MemberResponsibility,
    IReadOnlyList<ClaimLineDto> Lines,
    IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<StatusChangeDto> History);

public sealed record MemberSummaryDto(Guid Id, string MemberNumber, string FullName, string PlanName);

public sealed record ClaimLineDto(
    int LineNumber,
    string ProcedureCode,
    string? Modifier,
    DateOnly ServiceDate,
    int Units,
    decimal BilledAmount,
    ClaimLineStatus Status,
    decimal AllowedAmount,
    decimal DeductibleAmount,
    decimal CopayAmount,
    decimal CoinsuranceAmount,
    decimal PlanPaid,
    string? DenialCode,
    string? DenialDescription);

public sealed record FindingDto(string RuleName, FindingOutcome Outcome, string? ReasonCode, string Message, int? LineNumber);

public sealed record StatusChangeDto(ClaimStatus? From, ClaimStatus To, DateTimeOffset ChangedAt, string Actor, string? Note);

public sealed record MemberDto(
    Guid Id,
    string MemberNumber,
    string FullName,
    DateOnly DateOfBirth,
    string PlanCode,
    string PlanName,
    DateOnly CoverageStart,
    DateOnly? CoverageEnd,
    decimal DeductibleMet,
    decimal AnnualDeductible,
    decimal OutOfPocketMet,
    decimal OutOfPocketMax);

public sealed record ProviderDto(Guid Id, string Npi, string Name, string Specialty, bool IsInNetwork, bool IsActive);

public sealed record FeeScheduleDto(string ProcedureCode, string Description, decimal AllowedAmount, bool RequiresPriorAuthorization, bool IsOfficeVisit);

public sealed record AuditEntryDto(DateTimeOffset Timestamp, string Action, string Actor, string? Details);

public sealed record DashboardSummary(
    int TotalClaims,
    decimal TotalBilled,
    decimal TotalPaid,
    decimal AutoAdjudicationRate,
    decimal AverageDaysToAdjudicate,
    IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<DenialReasonCount> TopDenialReasons,
    IReadOnlyList<DailyVolume> DailyVolume);

public sealed record StatusCount(ClaimStatus Status, int Count, decimal Billed);

public sealed record DenialReasonCount(string ReasonCode, string Description, int Count);

public sealed record DailyVolume(DateOnly Date, int Received, int Finalized);
