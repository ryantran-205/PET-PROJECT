using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fnb.Infrastructure.Persistence;

/// <summary>All DateTime values are stored as UTC and materialised with DateTimeKind.Utc.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value.ToUniversalTime(), DateTimeKind.Utc),
    v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
