using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Domain.Claims;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Infrastructure.Persistence.Repositories;

internal sealed class ClaimRepository(ClaimFlowDbContext db) : IClaimRepository
{
    private static readonly ClaimStatus[] Finalized = [ClaimStatus.Approved, ClaimStatus.Denied, ClaimStatus.Paid];

    public Task<Claim?> GetAsync(Guid id, CancellationToken ct) =>
        db.Claims
            .Include(c => c.Lines)
            .Include(c => c.Findings)
            .Include(c => c.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public void Add(Claim claim) => db.Claims.Add(claim);

    public Task<bool> PatientControlNumberExistsAsync(Guid providerId, string patientControlNumber, CancellationToken ct) =>
        db.Claims.AnyAsync(c => c.ProviderId == providerId && c.PatientControlNumber == patientControlNumber, ct);

    public async Task<IReadOnlyList<ExistingServiceLine>> GetServiceHistoryAsync(
        Guid memberId, Guid providerId, DateOnly fromDate, DateOnly toDate, Guid excludeClaimId, CancellationToken ct) =>
        await (from c in db.Claims
               where c.MemberId == memberId && c.ProviderId == providerId && c.Id != excludeClaimId && c.Status != ClaimStatus.Denied
               from l in c.Lines
               where l.Status != ClaimLineStatus.Denied && l.ServiceDate >= fromDate && l.ServiceDate <= toDate
               select new ExistingServiceLine(c.ClaimNumber, l.ProcedureCode, l.Modifier, l.ServiceDate))
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Claim>> GetApprovedUnpaidAsync(int max, CancellationToken ct) =>
        await db.Claims
            .Include(c => c.Lines)
            .Include(c => c.History)
            .Where(c => c.Status == ClaimStatus.Approved)
            .OrderBy(c => c.AdjudicatedAt)
            .Take(max)
            .AsSplitQuery()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetIdsByStatusAsync(ClaimStatus status, CancellationToken ct) =>
        await db.Claims.Where(c => c.Status == status).OrderBy(c => c.SubmittedAt).Select(c => c.Id).ToListAsync(ct);

    public async Task<PagedResult<ClaimListItem>> SearchAsync(ClaimSearchCriteria criteria, CancellationToken ct)
    {
        var query =
            from c in db.Claims.AsNoTracking()
            join m in db.Members on c.MemberId equals m.Id
            join p in db.Providers on c.ProviderId equals p.Id
            select new { c, m, p };

        if (criteria.Status is { } status)
            query = query.Where(x => x.c.Status == status);

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim().ToUpper();
            query = query.Where(x =>
                x.c.ClaimNumber.Contains(term) ||
                x.m.MemberNumber.Contains(term) ||
                x.m.LastName.ToUpper().Contains(term) ||
                x.p.Npi.Contains(term) ||
                (x.c.PatientControlNumber != null && x.c.PatientControlNumber.ToUpper().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.c.SubmittedAt)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(x => new ClaimListItem(
                x.c.Id,
                x.c.ClaimNumber,
                x.m.MemberNumber,
                x.m.FirstName + " " + x.m.LastName,
                x.p.Name,
                x.c.Lines.Min(l => l.ServiceDate),
                x.c.Status,
                x.c.Source,
                x.c.TotalBilled,
                x.c.PlanPaid,
                x.c.SubmittedAt))
            .ToListAsync(ct);

        return new PagedResult<ClaimListItem>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<DashboardSummary> GetDashboardAsync(DateOnly today, CancellationToken ct)
    {
        var byStatus = await db.Claims
            .GroupBy(c => c.Status)
            .Select(g => new StatusCount(g.Key, g.Count(), g.Sum(c => c.TotalBilled)))
            .ToListAsync(ct);

        var totalPaid = await db.Claims.Where(c => c.Status == ClaimStatus.Paid).SumAsync(c => (decimal?)c.PlanPaid, ct) ?? 0m;

        var finalized = await db.Claims.CountAsync(c => Finalized.Contains(c.Status), ct);
        var finalizedAfterPend = await db.StatusHistory
            .Where(h => h.ToStatus == ClaimStatus.Pended)
            .Select(h => h.ClaimId)
            .Distinct()
            .CountAsync(id => db.Claims.Any(c => c.Id == id && Finalized.Contains(c.Status)), ct);
        var autoRate = finalized == 0 ? 0m : Math.Round((finalized - finalizedAfterPend) / (decimal)finalized * 100m, 1);

        var denials = await db.ClaimLines
            .Where(l => l.Status == ClaimLineStatus.Denied && l.DenialCode != null)
            .GroupBy(l => l.DenialCode!)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(5)
            .ToListAsync(ct);

        // Time-bucketed metrics are computed in memory over a bounded window so they work on any provider.
        var since = new DateTimeOffset(today.AddDays(-13).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var recent = await db.Claims
            .Where(c => c.SubmittedAt >= since || (c.AdjudicatedAt != null && c.AdjudicatedAt >= since))
            .Select(c => new { c.SubmittedAt, c.AdjudicatedAt })
            .ToListAsync(ct);

        var daily = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(i - 13))
            .Select(d => new DailyVolume(
                d,
                recent.Count(c => DateOnly.FromDateTime(c.SubmittedAt.UtcDateTime) == d),
                recent.Count(c => c.AdjudicatedAt is { } a && DateOnly.FromDateTime(a.UtcDateTime) == d)))
            .ToList();

        var turnaround = recent.Where(c => c.AdjudicatedAt is not null)
            .Select(c => (c.AdjudicatedAt!.Value - c.SubmittedAt).TotalDays)
            .DefaultIfEmpty(0)
            .Average();

        return new DashboardSummary(
            byStatus.Sum(s => s.Count),
            Math.Round(byStatus.Sum(s => s.Billed), 2),
            Math.Round(totalPaid, 2),
            autoRate,
            Math.Round((decimal)turnaround, 2),
            Enum.GetValues<ClaimStatus>()
                .Select(s => byStatus.FirstOrDefault(b => b.Status == s) ?? new StatusCount(s, 0, 0m))
                .Select(s => s with { Billed = Math.Round(s.Billed, 2) })
                .ToList(),
            denials.Select(d => new DenialReasonCount(d.Code, ReasonCodes.Describe(d.Code), d.Count)).ToList(),
            daily);
    }
}
