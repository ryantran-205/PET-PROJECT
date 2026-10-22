using Azure.Monitor.OpenTelemetry.AspNetCore;
using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Contracts;
using Fnb.Infrastructure.Caching;
using Fnb.Infrastructure.Messaging;
using Fnb.Infrastructure.Observability;
using Fnb.Infrastructure.Pdf;
using Fnb.Infrastructure.Persistence;
using Fnb.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Fnb.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        BindOptions(services, configuration);

        var sql = configuration.GetConnectionString("Sql");
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Sql' is required (set via user-secrets or environment variable ConnectionStrings__Sql).");
        }

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(sql, s => s.CommandTimeout(30)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ISequenceAllocator, SqlSequenceAllocator>();
        services.AddSingleton<IUniqueConstraintDetector, SqlUniqueConstraintDetector>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IAuditService, EfAuditService>();
        services.AddSingleton<IPasswordService, IdentityPasswordService>();
        services.AddSingleton<ITrackingTokenService, HmacTrackingTokenService>();
        services.AddSingleton<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<DevelopmentSeeder>();

        // Cache: Redis when configured; otherwise in-process memory (single instance / development only).
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "fnb:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, DistributedCacheService>();

        AddMessaging(services, configuration);
        return services;
    }

    private static void BindOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));
        services.Configure<AttendanceOptions>(configuration.GetSection(AttendanceOptions.Section));
        services.Configure<OrderingOptions>(configuration.GetSection(OrderingOptions.Section));
        services.Configure<ReportOptions>(configuration.GetSection(ReportOptions.Section));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.Section));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.Section));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.Section));
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration[$"{MessagingOptions.Section}:Provider"] ?? "InMemory";
        switch (provider.ToUpperInvariant())
        {
            case "INMEMORY":
                services.AddSingleton<InMemoryMessageBus>();
                services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<InMemoryMessageBus>());
                services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<InMemoryMessageBus>());
                break;
            case "RABBITMQ":
                services.AddSingleton<RabbitMqMessageBus>();
                services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RabbitMqMessageBus>());
                services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<RabbitMqMessageBus>());
                break;
            case "AZURESERVICEBUS":
                services.AddSingleton<ServiceBusMessageBus>();
                services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<ServiceBusMessageBus>());
                services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<ServiceBusMessageBus>());
                break;
            default:
                throw new InvalidOperationException($"Unknown Messaging:Provider '{provider}'. Use InMemory, RabbitMQ or AzureServiceBus.");
        }
    }

    /// <summary>Registers the outbox dispatcher (publishes committed integration events).</summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services)
    {
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxDispatcher>());
        return services;
    }

    /// <summary>Starts a consumer for a topic subscription. Handlers are resolved as <see cref="IMessageHandler"/> with a matching ConsumerName.</summary>
    public static IServiceCollection AddMessageConsumer(this IServiceCollection services, string subscription, string topic = Topics.OrderEvents)
    {
        services.AddSingleton<IHostedService>(sp => new MessageConsumerHostedService(
            topic,
            subscription,
            sp.GetRequiredService<IMessageSubscriber>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<MessageConsumerHostedService>>()));
        return services;
    }

    public static IServiceCollection AddMaintenance(this IServiceCollection services)
    {
        services.AddSingleton<MaintenanceService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<MaintenanceService>());
        return services;
    }

    /// <summary>
    /// OpenTelemetry → Azure Monitor when APPLICATIONINSIGHTS_CONNECTION_STRING is configured; otherwise metrics stay
    /// available through System.Diagnostics.Metrics (dotnet-counters) and logs go to the console.
    /// </summary>
    public static IServiceCollection AddFnbObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? configuration["AzureMonitor:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddOpenTelemetry()
                .UseAzureMonitor(o => o.ConnectionString = connectionString)
                .WithMetrics(m => m.AddMeter(FnbMetrics.MeterName))
                .WithTracing(t => t.AddSource(FnbMetrics.ActivitySourceName));
        }

        return services;
    }

    /// <summary>Applies pending EF Core migrations (used by the `--migrate` CLI switch and integration tests).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    public static async Task SeedDevelopmentDataAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().Override = SystemUser.Instance;
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(ct);
    }
}
