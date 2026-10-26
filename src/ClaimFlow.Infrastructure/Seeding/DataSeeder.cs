using ClaimFlow.Application;
using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Application.Contracts;
using ClaimFlow.Application.Services;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;
using ClaimFlow.Infrastructure.Persistence;
using ClaimFlow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Infrastructure.Seeding;

/// <summary>
/// Seeds synthetic reference data and demo claims. All names and identifiers are fictitious — no real PHI.
/// Demo claims are pushed through the real intake, adjudication, review and payment services with a
/// back-dated clock, so the dashboard shows realistic history produced by the actual business rules.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedReferenceDataAsync(ClaimFlowDbContext db, DateOnly today, CancellationToken ct = default)
    {
        if (await db.Plans.AnyAsync(ct)) return;

        var gold = new HealthPlan("GOLD-PPO", "Gold PPO", annualDeductible: 500m, coinsuranceRate: 0.20m,
            officeVisitCopay: 25m, outOfPocketMax: 4000m, timelyFilingDays: 180);
        var silver = new HealthPlan("SILVER-HMO", "Silver HMO", annualDeductible: 1500m, coinsuranceRate: 0.30m,
            officeVisitCopay: 40m, outOfPocketMax: 7000m, timelyFilingDays: 90);
        db.Plans.AddRange(gold, silver);

        var coverageStart = new DateOnly(today.Year - 1, 1, 1);
        Member M(string number, string first, string last, int birthYear, HealthPlan plan, DateOnly? start = null, DateOnly? end = null) =>
            new(number, first, last, new DateOnly(birthYear, 3, 14), plan, start ?? coverageStart, end);

        var members = new[]
        {
            M("M100001", "Jane", "Doe", 1988, gold),
            M("M100002", "John", "Smith", 1975, gold),
            M("M100003", "Maria", "Garcia", 1992, silver),
            M("M100004", "Wei", "Chen", 1983, silver),
            M("M100005", "Aisha", "Patel", 1969, gold),
            M("M100006", "Robert", "Johnson", 1958, silver, end: today.AddDays(-60)),
            M("M100007", "Emily", "Brown", 2001, gold, start: today.AddDays(-30)),
            M("M100008", "David", "Wilson", 1990, gold)
        };
        db.Members.AddRange(members);

        db.Providers.AddRange(
            new Provider("1234567893", "Riverside Family Medicine", "Family Medicine", isInNetwork: true),
            new Provider("1452369873", "Lakeside Orthopedics", "Orthopedic Surgery", isInNetwork: true),
            new Provider("1673849208", "Metro Imaging Center", "Radiology", isInNetwork: true),
            new Provider("1982736450", "Summit Cardiology Group", "Cardiology", isInNetwork: false),
            new Provider("1122334455", "Northside Urgent Care", "Urgent Care", isInNetwork: true, isActive: false),
            new Provider("1567890128", "Harbor Physical Therapy", "Physical Therapy", isInNetwork: true));

        db.FeeSchedule.AddRange(
            new FeeScheduleEntry("99202", "Office visit, new patient, straightforward", 75m, isOfficeVisit: true),
            new FeeScheduleEntry("99203", "Office visit, new patient, low complexity", 110m, isOfficeVisit: true),
            new FeeScheduleEntry("99213", "Office visit, established patient, low complexity", 92m, isOfficeVisit: true),
            new FeeScheduleEntry("99214", "Office visit, established patient, moderate complexity", 130m, isOfficeVisit: true),
            new FeeScheduleEntry("36415", "Routine venipuncture", 3m),
            new FeeScheduleEntry("80053", "Comprehensive metabolic panel", 14m),
            new FeeScheduleEntry("85025", "Complete blood count with differential", 11m),
            new FeeScheduleEntry("87880", "Strep A rapid test", 17m),
            new FeeScheduleEntry("90471", "Immunization administration", 20m),
            new FeeScheduleEntry("90686", "Influenza vaccine, quadrivalent", 22m),
            new FeeScheduleEntry("93000", "Electrocardiogram, complete", 18m),
            new FeeScheduleEntry("93306", "Echocardiography, complete", 210m),
            new FeeScheduleEntry("71046", "Chest X-ray, 2 views", 40m),
            new FeeScheduleEntry("73721", "MRI knee without contrast", 420m, requiresPriorAuthorization: true),
            new FeeScheduleEntry("70553", "MRI brain with and without contrast", 580m, requiresPriorAuthorization: true),
            new FeeScheduleEntry("29881", "Knee arthroscopy with meniscectomy", 850m, requiresPriorAuthorization: true),
            new FeeScheduleEntry("97110", "Therapeutic exercise, each 15 minutes", 32m),
            new FeeScheduleEntry("97140", "Manual therapy, each 15 minutes", 30m),
            new FeeScheduleEntry("J1100", "Dexamethasone sodium phosphate injection, 1 mg", 2m));

        db.PriorAuthorizations.AddRange(
            new PriorAuthorization(members[1].Id, "73721", "PA-2026-00412", today.AddDays(-60), today.AddDays(30)),
            new PriorAuthorization(members[4].Id, "29881", "PA-2026-00577", today.AddDays(-45), today.AddDays(45)));

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedDemoClaimsAsync(ClaimFlowDbContext db, AdjudicationEngine engine, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await db.Claims.AnyAsync(ct)) return;

        var clock = new SeedClock();
        var claims = new ClaimRepository(db);
        var members = new MemberRepository(db);
        var providers = new ProviderRepository(db);
        var audit = new AuditLog(db, clock);
        var uow = new UnitOfWork(db);
        var intake = new ClaimIntakeService(claims, members, providers, audit, uow, new NoOpQueue(), clock, new RandomClaimNumberGenerator(clock));
        var adjudication = new AdjudicationService(claims, members, providers, new ReferenceDataRepository(db), engine, audit, uow, clock);
        var payments = new PaymentService(claims, members, providers, audit, uow, clock);

        const string submitter = "seed:clearinghouse";
        const string reviewer = "seed:adjudicator";
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // (days ago submitted, member, provider NPI, PCN, diagnoses, lines as (CPT, service days ago, units, billed), review?)
        var scenarios = new (int Ago, string Member, string Npi, string Pcn, string[] Dx, (string Cpt, int SvcAgo, int Units, decimal Billed)[] Lines, ReviewDecision? Review)[]
        {
            (13, "M100001", "1234567893", "RFM-24001", ["J069"], [("99213", 14, 1, 150m), ("87880", 14, 1, 35m)], null),
            (13, "M100002", "1673849208", "MIC-55102", ["M2391"], [("73721", 15, 1, 1450m)], null),
            (12, "M100003", "1234567893", "RFM-24002", ["E119", "I10"], [("99214", 13, 1, 210m), ("80053", 13, 1, 45m), ("36415", 13, 1, 12m)], null),
            (12, "M100006", "1234567893", "RFM-24003", ["J209"], [("99213", 12, 1, 150m)], null),                     // terminated coverage
            (11, "M100004", "1673849208", "MIC-55103", ["G4489"], [("70553", 12, 1, 2400m)], null),                    // no prior auth
            (10, "M100005", "1452369873", "LKO-88001", ["S83241A"], [("29881", 11, 1, 12500m)], ReviewDecision.Approve), // high-dollar pend
            (10, "M100008", "1982736450", "SCG-30001", ["R002"], [("93000", 11, 1, 95m), ("93306", 11, 1, 850m)], ReviewDecision.Approve), // OON pend
            (9, "M100001", "1234567893", "RFM-24001-R", ["J069"], [("99213", 14, 1, 150m)], null),                    // duplicate of first claim
            (9, "M100002", "1567890128", "HPT-11001", ["M25561"], [("97110", 10, 3, 165m), ("97140", 10, 2, 110m)], null),
            (8, "M100003", "1122334455", "NUC-70001", ["J029"], [("99203", 9, 1, 180m)], null),                       // inactive provider
            (7, "M100004", "1234567893", "RFM-24004", ["Z23"], [("90471", 8, 1, 25m), ("90686", 8, 1, 40m)], null),
            (6, "M100007", "1234567893", "RFM-24005", ["Z0000"], [("99203", 7, 1, 180m), ("85025", 7, 1, 28m), ("36415", 7, 1, 12m)], null),
            (5, "M100003", "1234567893", "RFM-24006", ["R51"], [("99213", 200, 1, 150m)], null),                       // past timely filing
            (5, "M100005", "1982736450", "SCG-30002", ["I480"], [("93306", 6, 1, 900m)], ReviewDecision.Deny),
            (4, "M100008", "1234567893", "RFM-24007", ["J0190"], [("99214", 5, 1, 210m), ("99999", 5, 1, 60m)], null), // non-covered code on line 2
            (3, "M100002", "1567890128", "HPT-11002", ["M25561"], [("97110", 4, 2, 110m)], null),
            (2, "M100001", "1673849208", "MIC-55104", ["R0602"], [("71046", 3, 1, 120m)], null),
            (1, "M100004", "1452369873", "LKO-88002", ["M1711"], [("99203", 2, 1, 190m), ("J1100", 2, 10, 30m)], null),
            (1, "M100008", "1982736450", "SCG-30003", ["R0789"], [("93000", 2, 1, 95m)], null),                        // stays pended
            (0, "M100003", "1234567893", "RFM-24008", ["N390"], [("99213", 1, 1, 150m)], null)
        };

        var minute = 0;
        var paymentRunDone = false;
        foreach (var s in scenarios.OrderByDescending(s => s.Ago))
        {
            if (!paymentRunDone && s.Ago < 3)
            {
                // Weekly payment run three days ago pays everything approved up to then.
                clock.UtcNow = now.AddDays(-3);
                await payments.ReleasePaymentsAsync("seed:finance", ct);
                paymentRunDone = true;
            }

            var submittedAt = now.AddDays(-s.Ago).AddMinutes(-(scenarios.Length - minute++) * 7);
            clock.UtcNow = submittedAt;

            var result = await intake.SubmitAsync(new SubmitClaimCommand(
                s.Member, s.Npi, s.Pcn, s.Dx,
                s.Lines.Select(l => new SubmitClaimLine(l.Cpt, null, today.AddDays(-l.SvcAgo), l.Units, l.Billed)).ToList()),
                ClaimSource.X12Edi, submitter, ct);

            clock.UtcNow = submittedAt.AddMinutes(2);
            var status = await adjudication.AdjudicateAsync(result.Id, ct);

            if (status == ClaimStatus.Pended && s.Review is { } decision)
            {
                clock.UtcNow = submittedAt.AddHours(20);
                await adjudication.ReviewAsync(result.Id, new ReviewClaimCommand(decision,
                    decision == ReviewDecision.Approve ? "Medical records reviewed; services supported." : "Documentation does not support medical necessity.",
                    decision == ReviewDecision.Deny ? ReasonCodes.NotMedicallyNecessary : null), reviewer, ct);
            }
        }

        db.ChangeTracker.Clear();
    }

    private sealed class SeedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class NoOpQueue : IClaimQueue
    {
        public ValueTask EnqueueAsync(Guid claimId, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
