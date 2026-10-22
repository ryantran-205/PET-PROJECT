namespace Fnb.Domain.Common;

/// <summary>Base class for expected, business-level failures. Mapped to ProblemDetails by the API.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Business rule violated or invalid state transition (HTTP 409).</summary>
public sealed class BusinessRuleException(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    : AppException(code, message)
{
    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}

/// <summary>Request is syntactically valid but violates validation rules (HTTP 400).</summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation_failed", "One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationFailedException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Resource not found (HTTP 404).</summary>
public sealed class NotFoundException(string resource, object? id = null)
    : AppException("not_found", id is null ? $"{resource} was not found." : $"{resource} '{id}' was not found.");

/// <summary>Authenticated user lacks permission or branch scope (HTTP 403).</summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException("forbidden", message);

/// <summary>Concurrency or idempotency conflict (HTTP 409).</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>Credentials invalid or session expired (HTTP 401).</summary>
public sealed class UnauthorizedException(string message = "Authentication failed.") : AppException("unauthorized", message);
