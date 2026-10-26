using ClaimFlow.Domain.Common;

namespace ClaimFlow.Domain.Claims;

/// <summary>
/// Claim aggregate root. All status changes go through methods that enforce the workflow:
/// Received → InAdjudication → (Pended →) Approved | Denied → Paid (Approved only).
/// </summary>
public sealed class Claim : Entity
{
    public const int MaxLines = 50;
    public const int MaxDiagnosisCodes = 12;

    private readonly List<ClaimLine> _lines = [];
    private readonly List<AdjudicationFinding> _findings = [];
    private readonly List<ClaimStatusChange> _history = [];

    private Claim() { }

    public string ClaimNumber { get; private set; } = default!;
    public string? PatientControlNumber { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid ProviderId { get; private set; }
    public ClaimSource Source { get; private set; }
    public ClaimStatus Status { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string SubmittedBy { get; private set; } = default!;
    public DateTimeOffset? AdjudicatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? PendReason { get; private set; }
    public List<string> DiagnosisCodes { get; private set; } = [];

    public decimal TotalBilled { get; private set; }
    public decimal TotalAllowed { get; private set; }
    public decimal PlanPaid { get; private set; }
    public decimal MemberResponsibility { get; private set; }

    /// <summary>Optimistic concurrency token, rotated on every state change.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    public IReadOnlyList<ClaimLine> Lines => _lines;
    public IReadOnlyList<AdjudicationFinding> Findings => _findings;
    public IReadOnlyList<ClaimStatusChange> History => _history;

    public DateOnly ServiceDateFrom => _lines.Min(l => l.ServiceDate);
    public DateOnly ServiceDateTo => _lines.Max(l => l.ServiceDate);

    public static Claim Create(
        string claimNumber,
        Guid memberId,
        Guid providerId,
        ClaimSource source,
        IEnumerable<string> diagnosisCodes,
        IEnumerable<NewClaimLine> lines,
        DateTimeOffset submittedAt,
        string submittedBy,
        string? patientControlNumber = null)
    {
        var lineList = lines.ToList();
        var dxList = diagnosisCodes
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(NormalizeDiagnosis)
            .Distinct()
            .ToList();

        if (lineList.Count == 0) throw new DomainException("A claim must have at least one service line.");
        if (lineList.Count > MaxLines) throw new DomainException($"A claim cannot exceed {MaxLines} service lines.");
        if (dxList.Count == 0) throw new DomainException("At least one diagnosis code is required.");
        if (dxList.Count > MaxDiagnosisCodes) throw new DomainException($"A claim cannot exceed {MaxDiagnosisCodes} diagnosis codes.");
        if (lineList.Any(l => l.Units <= 0)) throw new DomainException("Units must be greater than zero.");
        if (lineList.Any(l => l.BilledAmount <= 0)) throw new DomainException("Billed amount must be greater than zero.");
        if (lineList.Any(l => DateOnly.FromDateTime(submittedAt.UtcDateTime) < l.ServiceDate))
            throw new DomainException("Service date cannot be in the future.");

        var claim = new Claim
        {
            ClaimNumber = claimNumber,
            MemberId = memberId,
            ProviderId = providerId,
            Source = source,
            DiagnosisCodes = dxList,
            SubmittedAt = submittedAt,
            SubmittedBy = submittedBy,
            ReceivedDate = DateOnly.FromDateTime(submittedAt.UtcDateTime),
            PatientControlNumber = string.IsNullOrWhiteSpace(patientControlNumber) ? null : patientControlNumber.Trim(),
            Status = ClaimStatus.Received
        };

        var n = 1;
        foreach (var line in lineList)
            claim._lines.Add(new ClaimLine(claim.Id, n++, line));

        claim.TotalBilled = claim._lines.Sum(l => l.BilledAmount);
        claim._history.Add(new ClaimStatusChange(claim.Id, null, ClaimStatus.Received, submittedAt, submittedBy, $"Received via {source}") { Sequence = 1 });
        return claim;
    }

    /// <summary>ICD-10-CM codes are stored without the decimal point, matching the X12 HI segment format.</summary>
    public static string NormalizeDiagnosis(string code) => code.Trim().Replace(".", string.Empty).ToUpperInvariant();

    public void BeginAdjudication(DateTimeOffset at, string actor)
    {
        EnsureStatus(ClaimStatus.Received);
        TransitionTo(ClaimStatus.InAdjudication, at, actor, null);
    }

    public void RecordFindings(IEnumerable<AdjudicationFinding> findings)
    {
        EnsureStatus(ClaimStatus.InAdjudication, ClaimStatus.Pended);
        foreach (var finding in findings)
        {
            finding.Sequence = _findings.Count + 1;
            _findings.Add(finding);
        }
    }

    public void Pend(string reason, DateTimeOffset at, string actor)
    {
        EnsureStatus(ClaimStatus.InAdjudication);
        PendReason = reason;
        TransitionTo(ClaimStatus.Pended, at, actor, reason);
    }

    /// <summary>Applies a decision to every line and moves the claim to Approved (any line payable) or Denied.</summary>
    public void Finalize(IReadOnlyCollection<LineDecision> decisions, DateTimeOffset at, string actor, string? note = null)
    {
        EnsureStatus(ClaimStatus.InAdjudication, ClaimStatus.Pended);

        var byLine = decisions.ToDictionary(d => d.LineNumber);
        if (byLine.Count != _lines.Count || _lines.Any(l => !byLine.ContainsKey(l.LineNumber)))
            throw new DomainException("A decision is required for every service line.");

        foreach (var line in _lines)
            line.Apply(byLine[line.LineNumber]);

        TotalAllowed = _lines.Sum(l => l.AllowedAmount);
        PlanPaid = _lines.Sum(l => l.PlanPaid);
        MemberResponsibility = _lines.Sum(l => l.MemberResponsibility);
        AdjudicatedAt = at;
        PendReason = null;

        var target = _lines.Any(l => l.Status == ClaimLineStatus.Approved) ? ClaimStatus.Approved : ClaimStatus.Denied;
        TransitionTo(target, at, actor, note);
    }

    public void MarkPaid(string paymentReference, DateTimeOffset at, string actor)
    {
        EnsureStatus(ClaimStatus.Approved);
        if (string.IsNullOrWhiteSpace(paymentReference)) throw new DomainException("Payment reference is required.");
        PaymentReference = paymentReference;
        PaidAt = at;
        TransitionTo(ClaimStatus.Paid, at, actor, $"Payment {paymentReference}");
    }

    private void TransitionTo(ClaimStatus to, DateTimeOffset at, string actor, string? note)
    {
        _history.Add(new ClaimStatusChange(Id, Status, to, at, actor, note) { Sequence = _history.Count + 1 });
        Status = to;
        Version = Guid.NewGuid();
    }

    private void EnsureStatus(params ClaimStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new DomainException($"Claim {ClaimNumber} is {Status}; expected {string.Join(" or ", allowed)}.");
    }
}

public sealed class ClaimStatusChange : Entity
{
    private ClaimStatusChange() { }

    public ClaimStatusChange(Guid claimId, ClaimStatus? from, ClaimStatus to, DateTimeOffset at, string actor, string? note)
    {
        ClaimId = claimId;
        FromStatus = from;
        ToStatus = to;
        ChangedAt = at;
        Actor = actor;
        Note = note;
    }

    public Guid ClaimId { get; private set; }
    public ClaimStatus? FromStatus { get; private set; }
    public ClaimStatus ToStatus { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string Actor { get; private set; } = default!;
    public string? Note { get; private set; }
    public int Sequence { get; internal set; }
}

/// <summary>Result of a single adjudication rule, persisted so reviewers can see why a claim was paid, pended or denied.</summary>
public sealed class AdjudicationFinding : Entity
{
    private AdjudicationFinding() { }

    public AdjudicationFinding(Guid claimId, string ruleName, FindingOutcome outcome, string? reasonCode, string message, int? lineNumber, DateTimeOffset createdAt)
    {
        ClaimId = claimId;
        RuleName = ruleName;
        Outcome = outcome;
        ReasonCode = reasonCode;
        Message = message;
        LineNumber = lineNumber;
        CreatedAt = createdAt;
    }

    public Guid ClaimId { get; private set; }
    public string RuleName { get; private set; } = default!;
    public FindingOutcome Outcome { get; private set; }
    public string? ReasonCode { get; private set; }
    public string Message { get; private set; } = default!;

    /// <summary>Null when the finding applies to the whole claim.</summary>
    public int? LineNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Order in which the finding was recorded on the claim (rule order, then manual review).</summary>
    public int Sequence { get; internal set; }
}
