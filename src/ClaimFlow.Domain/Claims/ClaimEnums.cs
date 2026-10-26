namespace ClaimFlow.Domain.Claims;

public enum ClaimStatus
{
    Received = 0,
    InAdjudication = 1,
    Pended = 2,
    Approved = 3,
    Denied = 4,
    Paid = 5
}

public enum ClaimLineStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2
}

public enum ClaimSource
{
    Api = 0,
    Portal = 1,
    X12Edi = 2
}

public enum FindingOutcome
{
    Pass = 0,
    Pend = 1,
    Deny = 2
}

/// <summary>
/// Subset of X12 Claim Adjustment Reason Codes (CARC) returned on remittances.
/// </summary>
public static class ReasonCodes
{
    public const string MissingOrInvalidInfo = "16";
    public const string Duplicate = "18";
    public const string BeforeCoverage = "26";
    public const string AfterCoverageTerminated = "27";
    public const string TimelyFilingExpired = "29";
    public const string NotMedicallyNecessary = "50";
    public const string NonCoveredService = "96";
    public const string ProviderNotEligible = "B7";
    public const string PriorAuthAbsent = "197";
    public const string OutOfNetworkReview = "242";
    public const string ExceedsFeeSchedule = "45";
    public const string ManualReview = "MR";

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        [MissingOrInvalidInfo] = "Claim lacks information or has submission/billing error(s).",
        [Duplicate] = "Exact duplicate claim/service.",
        [BeforeCoverage] = "Expenses incurred prior to coverage.",
        [AfterCoverageTerminated] = "Expenses incurred after coverage terminated.",
        [TimelyFilingExpired] = "The time limit for filing has expired.",
        [NotMedicallyNecessary] = "Non-covered: not deemed a medical necessity by the payer.",
        [NonCoveredService] = "Non-covered charge(s).",
        [ProviderNotEligible] = "Provider not certified/eligible to be paid for this service.",
        [PriorAuthAbsent] = "Precertification/authorization absent.",
        [OutOfNetworkReview] = "Services not provided by network/primary care providers.",
        [ExceedsFeeSchedule] = "Charge exceeds fee schedule/maximum allowable.",
        [ManualReview] = "Held for manual review."
    };

    public static string Describe(string code) =>
        Descriptions.TryGetValue(code, out var text) ? text : "Adjustment.";
}
