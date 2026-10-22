using System.Text;
using System.Text.Json.Serialization;
using Fnb.Api.Endpoints;
using Fnb.Api.Infrastructure;
using Fnb.Api.Realtime;
using Fnb.Api.Security;
using Fnb.Application;
using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Application.Orders;
using Fnb.Contracts;
using Fnb.Infrastructure;
using Fnb.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Fail-fast configuration validation (secrets must come from env / user-secrets / Key Vault) ----------
StartupValidation.Validate(config, builder.Environment);

// ---------- Core services ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddFnbObservability(config);
builder.Services.AddHttpCurrentUser();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddMemoryCache();
builder.Services.AddFnbProblemDetails();
builder.Services.AddFnbRateLimiting(config);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Container Apps / App Service ingress: trust the platform proxy. Restrict KnownNetworks in other topologies.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- AuthN / AuthZ ----------
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = JwtTokenService.ValidationParameters(jwt);
        o.Events = new JwtBearerEvents
        {
            // SignalR WebSockets cannot send headers: accept access_token query string ONLY on the hub path.
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = TokenRevocationCheck.ValidateAsync,
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Staff, p => p.RequireAuthenticatedUser().RequireClaim("role", "SuperAdmin", "BranchAdmin", "BranchStaff"))
    .AddPolicy(Policies.BranchAdmin, p => p.RequireAuthenticatedUser().RequireClaim("role", "SuperAdmin", "BranchAdmin"))
    .AddPolicy(Policies.SuperAdmin, p => p.RequireAuthenticatedUser().RequireClaim("role", "SuperAdmin"))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- CORS (explicit allowlist) ----------
var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithExposedHeaders("X-Correlation-Id", "Idempotent-Replayed", "Content-Disposition", "Retry-After")
    .AllowCredentials()));

// ---------- Realtime ----------
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(config["Azure:SignalR:ConnectionString"]))
{
    // Required for multi-instance scale-out: Azure SignalR Service owns client connections.
    signalR.AddAzureSignalR();
}

builder.Services.AddScoped<IMessageHandler, RealtimeRelayHandler>();
builder.Services.AddMessageConsumer(Subscriptions.Realtime);

// Development convenience: run the worker pipeline in-process (outbox dispatcher + order-processing consumer).
if (config.GetValue("Hosting:EnableInProcessWorker", false))
{
    builder.Services.AddScoped<IMessageHandler>(sp => sp.GetRequiredService<OrderProcessingHandler>());
    builder.Services.AddOutboxDispatcher();
    builder.Services.AddMessageConsumer(Subscriptions.OrderProcessing);
}

// ---------- Health & OpenAPI ----------
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sql", HealthStatus.Unhealthy, ["ready"]);
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------- CLI maintenance switches ----------
if (args.Contains("--migrate") || args.Contains("--migrate-only") || config.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigrateDatabaseAsync();
}

if (args.Contains("--seed") || args.Contains("--seed-only") || (app.Environment.IsDevelopment() && config.GetValue("Seed:Enabled", false)))
{
    await app.Services.SeedDevelopmentDataAsync();
}

if (args.Contains("--migrate-only") || args.Contains("--seed-only"))
{
    return;
}

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || config.GetValue("OpenApi:Enabled", false))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapBranchEndpoints();
api.MapHrEndpoints();
api.MapMenuInventoryEndpoints();
api.MapOrderEndpoints();

app.MapHub<OrdersHub>("/hubs/orders").AllowAnonymous();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;

internal static class StartupValidation
{
    public static void Validate(IConfiguration config, IWebHostEnvironment env)
    {
        var errors = new List<string>();
        var signingKey = config["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            errors.Add("Jwt:SigningKey must be set to at least 32 bytes (env Jwt__SigningKey or user-secrets).");
        }

        var tracking = config["Ordering:TrackingTokenSecret"];
        if (string.IsNullOrWhiteSpace(tracking) || tracking.Length < 32)
        {
            errors.Add("Ordering:TrackingTokenSecret must be set to at least 32 characters.");
        }

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Sql")))
        {
            errors.Add("ConnectionStrings:Sql is required.");
        }

        if (!env.IsDevelopment() && string.Equals(config["Messaging:Provider"] ?? "InMemory", "InMemory", StringComparison.OrdinalIgnoreCase)
            && !config.GetValue("Messaging:AllowInMemoryOutsideDevelopment", false))
        {
            errors.Add("Messaging:Provider=InMemory is not durable and is only allowed in Development. Use RabbitMQ or AzureServiceBus.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid configuration:\n - " + string.Join("\n - ", errors));
        }
    }
}
