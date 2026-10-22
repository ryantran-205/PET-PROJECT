using System.Text.Json;
using Fnb.Application.Abstractions;
using Fnb.Contracts;
using Fnb.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Infrastructure.Persistence;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Concurrency-safe number allocation using MERGE … WITH (HOLDLOCK) on (Scope, BranchId, BusinessDate).
/// Runs inside the caller's transaction, so a rolled-back order also rolls back its number; numbers of cancelled
/// orders are never reused. Never uses MAX()+1.
/// </summary>
public sealed class SqlSequenceAllocator(AppDbContext db) : ISequenceAllocator
{
    private const string Sql = """
        MERGE [NumberSequences] WITH (HOLDLOCK) AS t
        USING (SELECT @scope AS Scope, @branchId AS BranchId, @date AS BusinessDate) AS s
            ON t.Scope = s.Scope AND t.BranchId = s.BranchId AND t.BusinessDate = s.BusinessDate
        WHEN MATCHED THEN UPDATE SET LastNumber = t.LastNumber + 1
        WHEN NOT MATCHED THEN INSERT (Scope, BranchId, BusinessDate, LastNumber) VALUES (s.Scope, s.BranchId, s.BusinessDate, 1)
        OUTPUT inserted.LastNumber AS [Value];
        """;

    public async Task<int> NextAsync(string scope, Guid branchId, DateOnly businessDate, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Sequence allocation must run inside a transaction.");
        }

        var result = await db.Database.SqlQueryRaw<int>(Sql,
                new SqlParameter("@scope", scope),
                new SqlParameter("@branchId", branchId),
                new SqlParameter("@date", System.Data.SqlDbType.Date) { Value = businessDate.ToDateTime(TimeOnly.MinValue) })
            .ToListAsync(ct);
        return result.Single();
    }
}

public sealed class SqlUniqueConstraintDetector : IUniqueConstraintDetector
{
    public bool IsUniqueViolation(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(err => err.Number is 2601 or 2627))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class EfOutbox(AppDbContext db, IClock clock) : IOutbox
{
    public void Enqueue<T>(string topic, string type, T payload, string? correlationId = null)
        where T : notnull
    {
        var now = clock.UtcNow;
        db.OutboxMessages.Add(new OutboxMessage
        {
            Topic = topic,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, MessagingJson.Options),
            CorrelationId = correlationId,
            OccurredAtUtc = now,
            NextAttemptAtUtc = now,
        });
    }
}

public sealed class EfAuditService(AppDbContext db, ICurrentUser currentUser, IClock clock) : IAuditService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public void Record(string action, string resourceType, string? resourceId, Guid? branchId, object? changes = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = currentUser.UserId,
            ActorName = currentUser.IsAuthenticated ? currentUser.UserName : "anonymous",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BranchId = branchId,
            TimestampUtc = clock.UtcNow,
            CorrelationId = currentUser.CorrelationId,
            IpAddress = currentUser.IpAddress,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, Json),
        });
    }
}
