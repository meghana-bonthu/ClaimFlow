namespace ClaimFlow.Application.Common;

/// <summary>Mapped to HTTP 404 by the API.</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.");

/// <summary>Mapped to HTTP 400 (validation problem details) by the API.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors.AsReadOnly();

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Mapped to HTTP 409 by the API (duplicate submission, concurrent update, invalid state).</summary>
public sealed class ConflictException(string message) : Exception(message);
