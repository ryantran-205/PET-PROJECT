using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Fnb.Api.Infrastructure;

public static class RateLimits
{
    public const string Login = "login";
    public const string PublicOrder = "public-order";
    public const string PublicRead = "public-read";

    public static IServiceCollection AddFnbRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        var loginPerMinute = section.GetValue("LoginPerMinute", 10);
        var orderPerMinute = section.GetValue("PublicOrderPerMinute", 20);
        var readPerMinute = section.GetValue("PublicReadPerMinute", 240);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var problems = ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = ctx.HttpContext,
                    ProblemDetails = { Status = 429, Title = "Quá nhiều yêu cầu", Detail = "Bạn thao tác quá nhanh, vui lòng thử lại sau." },
                });
            };
            o.AddPolicy(Login, ctx => Partition(ctx, Login, loginPerMinute));
            o.AddPolicy(PublicOrder, ctx => Partition(ctx, PublicOrder, orderPerMinute));
            o.AddPolicy(PublicRead, ctx => Partition(ctx, PublicRead, readPerMinute));
        });
        return services;
    }

    /// <summary>Per client IP fixed window. Behind a proxy, configure ForwardedHeaders so RemoteIpAddress is the real client.</summary>
    private static RateLimitPartition<string> Partition(HttpContext ctx, string policy, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter($"{policy}:{ctx.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
