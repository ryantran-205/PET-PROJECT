using System.Text.Json;
using Fnb.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Fnb.Infrastructure.Caching;

/// <summary>
/// JSON cache over IDistributedCache (Redis in deployed environments, in-memory locally).
/// Failures are logged and treated as cache misses — the database remains the source of truth.
/// </summary>
public sealed class DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var bytes = await cache.GetAsync(key, cts.Token);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Cache read failed for {CacheKey}; falling back to database", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Cache write failed for {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await cache.RemoveAsync(key, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Stale entries expire by TTL; log so operators notice cache outages.
            logger.LogWarning(ex, "Cache invalidation failed for {CacheKey}; entry will expire by TTL", key);
        }
    }
}
