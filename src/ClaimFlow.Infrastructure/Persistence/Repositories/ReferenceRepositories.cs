using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Common;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Audit;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Infrastructure.Persistence.Repositories;

internal sealed class MemberRepository(ClaimFlowDbContext db) : IMemberRepository
{
    public Task<Member?> GetAsync(Guid id, CancellationToken ct) =>
        db.Members.Include(m => m.Plan).FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<Member?> GetByNumberAsync(string memberNumber, CancellationToken ct)
    {
        var normalized = memberNumber.Trim().ToUpperInvariant();
        return db.Members.Include(m => m.Plan).FirstOrDefaultAsync(m => m.MemberNumber == normalized, ct);
    }

    public async Task<IReadOnlyList<MemberDto>> SearchAsync(string? search, CancellationToken ct)
    {
        var query = db.Members.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(m => m.MemberNumber.Contains(term) || m.LastName.ToUpper().Contains(term) || m.FirstName.ToUpper().Contains(term));
        }

        return await query
            .OrderBy(m => m.MemberNumber)
            .Take(50)
            .Select(m => new MemberDto(
                m.Id, m.MemberNumber, m.FirstName + " " + m.LastName, m.DateOfBirth,
                m.Plan!.Code, m.Plan.Name, m.CoverageStart, m.CoverageEnd,
                m.DeductibleMet, m.Plan.AnnualDeductible, m.OutOfPocketMet, m.Plan.OutOfPocketMax))
            .ToListAsync(ct);
    }
}

internal sealed class ProviderRepository(ClaimFlowDbContext db) : IProviderRepository
{
    public Task<Provider?> GetAsync(Guid id, CancellationToken ct) => db.Providers.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Provider?> GetByNpiAsync(string npi, CancellationToken ct) => db.Providers.FirstOrDefaultAsync(p => p.Npi == npi.Trim(), ct);

    public async Task<IReadOnlyList<ProviderDto>> SearchAsync(string? search, CancellationToken ct)
    {
        var query = db.Providers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(p => p.Npi.Contains(term) || p.Name.ToUpper().Contains(term));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(50)
            .Select(p => new ProviderDto(p.Id, p.Npi, p.Name, p.Specialty, p.IsInNetwork, p.IsActive))
            .ToListAsync(ct);
    }
}

internal sealed class ReferenceDataRepository(ClaimFlowDbContext db) : IReferenceDataRepository
{
    public async Task<IReadOnlyDictionary<string, FeeScheduleEntry>> GetFeeScheduleAsync(IEnumerable<string> procedureCodes, CancellationToken ct)
    {
        var codes = procedureCodes.Select(c => c.ToUpperInvariant()).Distinct().ToList();
        return await db.FeeSchedule.AsNoTracking().Where(f => codes.Contains(f.ProcedureCode)).ToDictionaryAsync(f => f.ProcedureCode, ct);
    }

    public async Task<IReadOnlyList<FeeScheduleDto>> ListFeeScheduleAsync(CancellationToken ct) =>
        await db.FeeSchedule.AsNoTracking()
            .OrderBy(f => f.ProcedureCode)
            .Select(f => new FeeScheduleDto(f.ProcedureCode, f.Description, f.AllowedAmount, f.RequiresPriorAuthorization, f.IsOfficeVisit))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PriorAuthorization>> GetPriorAuthorizationsAsync(Guid memberId, CancellationToken ct) =>
        await db.PriorAuthorizations.AsNoTracking().Where(a => a.MemberId == memberId).ToListAsync(ct);
}

internal sealed class AuditLog(ClaimFlowDbContext db, IClock clock) : IAuditLog
{
    public void Record(string entityType, string entityKey, string action, string actor, string? details = null) =>
        db.AuditEntries.Add(new AuditEntry(entityType, entityKey, action, actor, clock.UtcNow, details));

    public async Task<IReadOnlyList<AuditEntryDto>> GetForEntityAsync(string entityType, string entityKey, CancellationToken ct) =>
        await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityKey == entityKey)
            .OrderBy(a => a.Timestamp)
            .Select(a => new AuditEntryDto(a.Timestamp, a.Action, a.Actor, a.Details))
            .ToListAsync(ct);
}

internal sealed class UnitOfWork(ClaimFlowDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The record was modified by another process. Reload and try again.");
        }
    }
}
