using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.Application.Abstractions;

public interface IClaimRepository
{
    /// <summary>Loads the full aggregate (lines, findings, history) for update.</summary>
    Task<Claim?> GetAsync(Guid id, CancellationToken ct);

    void Add(Claim claim);

    Task<bool> PatientControlNumberExistsAsync(Guid providerId, string patientControlNumber, CancellationToken ct);

    /// <summary>Non-denied service lines on other claims for the same member/provider within a date window.</summary>
    Task<IReadOnlyList<ExistingServiceLine>> GetServiceHistoryAsync(
        Guid memberId, Guid providerId, DateOnly fromDate, DateOnly toDate, Guid excludeClaimId, CancellationToken ct);

    Task<IReadOnlyList<Claim>> GetApprovedUnpaidAsync(int max, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(ClaimStatus status, CancellationToken ct);

    Task<PagedResult<ClaimListItem>> SearchAsync(ClaimSearchCriteria criteria, CancellationToken ct);

    Task<DashboardSummary> GetDashboardAsync(DateOnly today, CancellationToken ct);
}

public interface IMemberRepository
{
    Task<Member?> GetAsync(Guid id, CancellationToken ct);
    Task<Member?> GetByNumberAsync(string memberNumber, CancellationToken ct);
    Task<IReadOnlyList<MemberDto>> SearchAsync(string? search, CancellationToken ct);
}

public interface IProviderRepository
{
    Task<Provider?> GetAsync(Guid id, CancellationToken ct);
    Task<Provider?> GetByNpiAsync(string npi, CancellationToken ct);
    Task<IReadOnlyList<ProviderDto>> SearchAsync(string? search, CancellationToken ct);
}

public interface IReferenceDataRepository
{
    Task<IReadOnlyDictionary<string, FeeScheduleEntry>> GetFeeScheduleAsync(IEnumerable<string> procedureCodes, CancellationToken ct);
    Task<IReadOnlyList<FeeScheduleDto>> ListFeeScheduleAsync(CancellationToken ct);
    Task<IReadOnlyList<PriorAuthorization>> GetPriorAuthorizationsAsync(Guid memberId, CancellationToken ct);
}

/// <summary>Writes HIPAA audit-trail records in the same transaction as the business change.</summary>
public interface IAuditLog
{
    void Record(string entityType, string entityKey, string action, string actor, string? details = null);
    Task<IReadOnlyList<AuditEntryDto>> GetForEntityAsync(string entityType, string entityKey, CancellationToken ct);
}

public interface IUnitOfWork
{
    /// <summary>Throws <see cref="Common.ConflictException"/> on optimistic concurrency failures.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Hands claims off to the background adjudication worker.</summary>
public interface IClaimQueue
{
    ValueTask EnqueueAsync(Guid claimId, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IClaimNumberGenerator
{
    string Next();
}
