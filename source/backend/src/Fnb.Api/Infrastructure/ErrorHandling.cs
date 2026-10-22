using System.Diagnostics;
using System.Text.RegularExpressions;
using Fnb.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails. Never leaks stack traces or internal messages for unexpected errors.</summary>
public sealed partial class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = exception switch
        {
            ValidationFailedException => (StatusCodes.Status400BadRequest, "validation_failed", "Dữ liệu không hợp lệ", "Một hoặc nhiều trường không hợp lệ."),
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Code, "Chưa xác thực", e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, "Không có quyền", e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, "Không tìm thấy", e.Message),
            BusinessRuleException e => (StatusCodes.Status409Conflict, e.Code, "Vi phạm quy tắc nghiệp vụ", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, "Xung đột dữ liệu", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency_conflict", "Xung đột dữ liệu",
                "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại."),
            BadHttpRequestException e => (e.StatusCode, "bad_request", "Yêu cầu không hợp lệ", "Yêu cầu không đúng định dạng."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "client_closed", "Đã hủy", "Yêu cầu đã bị hủy."),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "Lỗi hệ thống", "Đã xảy ra lỗi không mong muốn. Vui lòng thử lại sau."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status} {Code}: {Message}", status, code, exception.Message);
        }

        if (status == 499)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        httpContext.Response.StatusCode = status;
        var problem = exception is ValidationFailedException v
            ? new ValidationProblemDetails(v.Errors.ToDictionary(k => k.Key, k => k.Value)) { Title = title, Detail = detail, Status = status }
            : new ProblemDetails { Title = title, Detail = detail, Status = status };
        problem.Type = $"https://fnb.local/problems/{code}";
        problem.Extensions["code"] = code;
        if (exception is BusinessRuleException { Details: { } details })
        {
            foreach (var (key, value) in details)
            {
                problem.Extensions[key] = value;
            }
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}

/// <summary>
/// Propagates X-Correlation-Id (validated) or generates one; exposes it as HttpContext.TraceIdentifier, the response
/// header, the logging scope and the current Activity tag.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,64}$")]
    private static partial Regex Valid();

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var correlationId = Valid().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("fnb.correlation_id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = correlationId;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddFnbProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
            ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier;
            if (ctx.ProblemDetails.Status == StatusCodes.Status401Unauthorized && !ctx.ProblemDetails.Extensions.ContainsKey("code"))
            {
                ctx.ProblemDetails.Extensions["code"] = "unauthorized";
            }
            else if (ctx.ProblemDetails.Status == StatusCodes.Status403Forbidden && !ctx.ProblemDetails.Extensions.ContainsKey("code"))
            {
                ctx.ProblemDetails.Extensions["code"] = "forbidden";
            }
            else if (ctx.ProblemDetails.Status == StatusCodes.Status429TooManyRequests)
            {
                ctx.ProblemDetails.Extensions["code"] = "rate_limited";
            }
        });
        services.AddExceptionHandler<AppExceptionHandler>();
        return services;
    }
}
