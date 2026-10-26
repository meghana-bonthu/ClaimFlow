using ClaimFlow.Application.Adjudication;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Plans;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.UnitTests;

/// <summary>Builders for synthetic test data. "Today" is fixed so tests are deterministic.</summary>
internal static class TestData
{
    public static readonly DateOnly Today = new(2026, 10, 20);
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    public const string InNetworkNpi = "1234567893";
    public const string OutOfNetworkNpi = "1982736450";

    public static HealthPlan GoldPlan() => new("GOLD", "Gold PPO", 500m, 0.20m, 25m, 4000m, 180);

    public static Member Member(HealthPlan? plan = null, DateOnly? coverageStart = null, DateOnly? coverageEnd = null) =>
        new("M100001", "Test", "Member", new DateOnly(1990, 1, 1), plan ?? GoldPlan(), coverageStart ?? new DateOnly(2026, 1, 1), coverageEnd);

    public static Provider Provider(bool inNetwork = true, bool active = true) =>
        new(inNetwork ? InNetworkNpi : OutOfNetworkNpi, "Test Clinic", "Family Medicine", inNetwork, active);

    public static FeeScheduleEntry OfficeVisit => new("99213", "Office visit", 92m, isOfficeVisit: true);
    public static FeeScheduleEntry Lab => new("80053", "Metabolic panel", 14m);
    public static FeeScheduleEntry Mri => new("73721", "MRI knee", 420m, requiresPriorAuthorization: true);

    public static Dictionary<string, FeeScheduleEntry> FeeSchedule(params FeeScheduleEntry[] entries) =>
        (entries.Length == 0 ? [OfficeVisit, Lab, Mri] : entries).ToDictionary(e => e.ProcedureCode);

    public static NewClaimLine Line(string code = "99213", int daysAgo = 5, decimal billed = 150m, int units = 1, string? modifier = null) =>
        new(code, Today.AddDays(-daysAgo), units, billed, modifier);

    public static Claim Claim(Member? member = null, Provider? provider = null, string[]? dx = null, params NewClaimLine[] lines) =>
        ClaimFlow.Domain.Claims.Claim.Create(
            "CLM-TEST-000001",
            (member ?? Member()).Id,
            (provider ?? Provider()).Id,
            ClaimSource.Api,
            dx ?? ["J069"],
            lines.Length == 0 ? [Line()] : lines,
            Now,
            "tester");

    public static AdjudicationContext Context(
        Claim? claim = null,
        Member? member = null,
        Provider? provider = null,
        IReadOnlyDictionary<string, FeeScheduleEntry>? fees = null,
        IReadOnlyList<PriorAuthorization>? auths = null,
        IReadOnlyList<ExistingServiceLine>? history = null)
    {
        member ??= Member();
        provider ??= Provider();
        claim ??= Claim(member, provider);
        return new AdjudicationContext(claim, member, member.Plan!, provider, fees ?? FeeSchedule(), auths ?? [], history ?? []);
    }

    public static Claim InAdjudication(this Claim claim)
    {
        claim.BeginAdjudication(Now, "test");
        return claim;
    }
}
