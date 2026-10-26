namespace ClaimFlow.Domain.Common;

/// <summary>Base type for all aggregates and entities. Ids are generated client-side so they are known before persistence.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>Raised when a business invariant is violated (e.g. an invalid claim status transition).</summary>
public sealed class DomainException(string message) : Exception(message);
