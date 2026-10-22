using System.Security.Cryptography;
using System.Text;
using Fnb.Domain.Common;

namespace Fnb.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 200;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

/// <summary>Small fluent validation helper. Collects all errors then throws <see cref="ValidationFailedException"/>.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public bool HasErrors => _errors.Count > 0;

    public Validator Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
        return this;
    }

    public Validator Must(bool condition, string field, string message) => condition ? this : Add(field, message);

    public Validator Required(string? value, string field, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Add(field, "Trường này là bắt buộc.");
        }

        return MaxLength(value, field, maxLength);
    }

    public Validator MaxLength(string? value, string field, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            Add(field, $"Tối đa {maxLength} ký tự.");
        }

        return this;
    }

    public Validator Range(decimal value, decimal min, decimal max, string field) =>
        Must(value >= min && value <= max, field, $"Giá trị phải trong khoảng {min} – {max}.");

    public Validator Range(int value, int min, int max, string field) =>
        Must(value >= min && value <= max, field, $"Giá trị phải trong khoảng {min} – {max}.");

    public Validator NotEmpty(Guid value, string field) => Must(value != Guid.Empty, field, "Trường này là bắt buộc.");

    public void ThrowIfInvalid()
    {
        if (HasErrors)
        {
            throw new ValidationFailedException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
        }
    }
}

public static class SecureTokens
{
    /// <summary>Cryptographically random URL-safe token (default 256-bit entropy).</summary>
    public static string Create(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}

public static class BusinessTime
{
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows fallback name for Asia/Ho_Chi_Minh if ICU is unavailable.
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    public static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static DateTime ToLocal(DateTime utc, string timeZoneId) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Resolve(timeZoneId));

    public static DateOnly BusinessDate(DateTime utc, string timeZoneId) => DateOnly.FromDateTime(ToLocal(utc, timeZoneId));

    /// <summary>UTC range [start, end) covering local dates from..to inclusive.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) UtcRange(DateOnly from, DateOnly to, string timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), tz);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), tz);
        return (start, end);
    }
}
