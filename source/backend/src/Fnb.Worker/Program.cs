using Fnb.Application;
using Fnb.Application.Abstractions;
using Fnb.Application.Orders;
using Fnb.Contracts;
using Fnb.Infrastructure;
using Fnb.Infrastructure.Messaging;

// Fnb.Worker: outbox dispatcher + order-processing consumer + retention maintenance.
// Scale horizontally: outbox claims use UPDLOCK/READPAST; consumers are idempotent (inbox table).
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

if (string.IsNullOrWhiteSpace(config["Ordering:TrackingTokenSecret"]) || config["Ordering:TrackingTokenSecret"]!.Length < 32)
{
    throw new InvalidOperationException("Ordering:TrackingTokenSecret (>= 32 chars) is required.");
}

if (!builder.Environment.IsDevelopment() && string.Equals(config["Messaging:Provider"] ?? "InMemory", "InMemory", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("The standalone worker requires a durable broker (Messaging:Provider = RabbitMQ or AzureServiceBus).");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddFnbObservability(config);
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>().Override ?? SystemUser.Instance);
builder.Services.AddScoped<ITokenService, NoTokenService>();
builder.Services.AddScoped<IMessageHandler>(sp => sp.GetRequiredService<OrderProcessingHandler>());
builder.Services.AddOutboxDispatcher();
builder.Services.AddMessageConsumer(Subscriptions.OrderProcessing);
builder.Services.AddMaintenance();

var host = builder.Build();
if (args.Contains("--migrate"))
{
    await host.Services.MigrateDatabaseAsync();
}

await host.RunAsync();

/// <summary>The worker never issues user tokens; registered only to satisfy AuthService's dependency graph.</summary>
internal sealed class NoTokenService : ITokenService
{
    public AccessToken CreateAccessToken(Fnb.Domain.Identity.User user, IReadOnlyCollection<Guid> branchIds) =>
        throw new NotSupportedException("Token issuance is not available in the worker.");
}
