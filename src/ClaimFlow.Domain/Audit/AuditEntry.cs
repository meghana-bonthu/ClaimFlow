using ClaimFlow.Domain.Common;

namespace ClaimFlow.Domain.Audit;

/// <summary>
/// Immutable audit record (HIPAA §164.312(b) audit controls). Details must never contain PHI —
/// store identifiers like claim numbers, not member names or dates of birth.
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry() { }

    public AuditEntry(string entityType, string entityKey, string action, string actor, DateTimeOffset timestamp, string? details = null)
    {
        EntityType = entityType;
        EntityKey = entityKey;
        Action = action;
        Actor = actor;
        Timestamp = timestamp;
        Details = details;
    }

    public string EntityType { get; private set; } = default!;
    public string EntityKey { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string Actor { get; private set; } = default!;
    public DateTimeOffset Timestamp { get; private set; }
    public string? Details { get; private set; }
}
