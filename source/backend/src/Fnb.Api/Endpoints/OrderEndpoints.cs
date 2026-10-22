using Fnb.Api.Infrastructure;
using Fnb.Api.Security;
using Fnb.Application.Abstractions;
using Fnb.Application.Orders;
using Fnb.Application.Menu;
using Fnb.Application.Reports;
using Fnb.Domain.Common;

namespace Fnb.Api.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // ----- Public (anonymous) ordering — rate limited, minimal data exposure -----
        var pub = app.MapGroup("/public").WithTags("Public ordering").AllowAnonymous();
        pub.MapGet("/branches/{branchCode}/menu", (string branchCode, string? table, PublicMenuService svc, CancellationToken ct) => svc.GetAsync(branchCode, table, ct))
            .RequireRateLimiting(RateLimits.PublicRead)
            .WithSummary("Menu công khai của chi nhánh (đã lọc món đang bán, giá theo chi nhánh). Có cache.");
        pub.MapPost("/orders", async (HttpContext http, CreatePublicOrderRequest request, PublicOrderService svc, CancellationToken ct) =>
            {
                var result = await svc.CreateAsync(request, http.Request.Headers[IdempotencyHeader].ToString(), ct);
                if (result.Replayed)
                {
                    http.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Ok(result.Order);
                }

                return Results.Created($"/api/v1/public/orders/{result.Order.TrackingToken}", result.Order);
            })
            .RequireRateLimiting(RateLimits.PublicOrder)
            .WithSummary("Tạo đơn công khai. Bắt buộc header Idempotency-Key; giá luôn được tính lại phía server.")
            .Produces<PublicOrderDto>(StatusCodes.Status201Created).Produces<PublicOrderDto>()
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        pub.MapGet("/branches/{branchCode}/queue", (string branchCode, PublicOrderService svc, CancellationToken ct) => svc.GetQueueAsync(branchCode, ct))
            .RequireRateLimiting(RateLimits.PublicRead)
            .WithSummary("Màn hình gọi số: danh sách số đơn đang xử lý trong ngày (chỉ số đơn + trạng thái).");
        pub.MapGet("/orders/{token}", (string token, PublicOrderService svc, CancellationToken ct) => svc.GetByTokenAsync(token, ct))
            .RequireRateLimiting(RateLimits.PublicRead).WithSummary("Theo dõi đơn bằng tracking token (không lộ dữ liệu nội bộ).");
        pub.MapPost("/orders/{token}/cancel", (string token, CancelPublicOrderRequest request, PublicOrderService svc, CancellationToken ct) =>
                svc.CancelByTokenAsync(token, request, ct))
            .RequireRateLimiting(RateLimits.PublicOrder).WithSummary("Khách hủy đơn — chỉ khi đơn chưa được xác nhận.");

        // ----- Staff order processing -----
        app.MapGet("/branches/{branchId:guid}/orders", (Guid branchId, [AsParameters] OrderQuery query, OrderService svc, CancellationToken ct) =>
                svc.ListAsync(branchId, query, ct))
            .WithTags("Orders").RequireAuthorization(Policies.Staff);

        var orders = app.MapGroup("/orders/{orderId:guid}").WithTags("Orders").RequireAuthorization(Policies.Staff);
        orders.MapGet("/", (Guid orderId, OrderService svc, CancellationToken ct) => svc.GetAsync(orderId, ct));
        orders.MapPost("/confirm", (Guid orderId, OrderActionRequest? r, OrderService svc, CancellationToken ct) => svc.ConfirmAsync(orderId, r ?? Empty, ct))
            .WithSummary("Xác nhận đơn: trừ kho nguyên tử theo công thức (409 insufficient_stock nếu thiếu).");
        orders.MapPost("/start-preparing", (Guid orderId, OrderActionRequest? r, OrderService svc, CancellationToken ct) => svc.StartPreparingAsync(orderId, r ?? Empty, ct));
        orders.MapPost("/mark-ready", (Guid orderId, OrderActionRequest? r, OrderService svc, CancellationToken ct) => svc.MarkReadyAsync(orderId, r ?? Empty, ct));
        orders.MapPost("/complete", (Guid orderId, OrderActionRequest? r, OrderService svc, CancellationToken ct) => svc.CompleteAsync(orderId, r ?? Empty, ct));
        orders.MapPost("/cancel", (Guid orderId, OrderActionRequest r, OrderService svc, CancellationToken ct) => svc.CancelAsync(orderId, r, ct))
            .WithSummary("Hủy đơn (bắt buộc lý do). Đơn đã trừ kho sẽ được hoàn kho qua bút toán đảo.");
        orders.MapPost("/reject", (Guid orderId, OrderActionRequest r, OrderService svc, CancellationToken ct) => svc.RejectAsync(orderId, r, ct))
            .WithSummary("Từ chối đơn chờ xác nhận (bắt buộc lý do).");
        orders.MapPost("/payments", (Guid orderId, RecordPaymentRequest r, OrderService svc, CancellationToken ct) => svc.RecordPaymentAsync(orderId, r, ct))
            .WithSummary("Ghi nhận thanh toán nội bộ (tiền mặt / chuyển khoản QR). Không tích hợp cổng thanh toán.");
        orders.MapPost("/refund", (Guid orderId, RefundRequest r, OrderService svc, CancellationToken ct) => svc.RefundAsync(orderId, r, ct))
            .RequireAuthorization(Policies.BranchAdmin);
        orders.MapPost("/invoice", (Guid orderId, InvoiceService svc, CancellationToken ct) => svc.EnsureForOrderAsDtoAsync(orderId, ct))
            .WithSummary("Lấy hoặc tạo hóa đơn nội bộ cho đơn đã xác nhận (idempotent).");

        // ----- Invoices -----
        var invoices = app.MapGroup("/invoices").WithTags("Invoices").RequireAuthorization(Policies.Staff);
        invoices.MapGet("/", ([AsParameters] InvoiceQuery q, InvoiceService svc, CancellationToken ct) => svc.ListAsync(q, ct));
        invoices.MapGet("/{invoiceId:guid}", (Guid invoiceId, InvoiceService svc, CancellationToken ct) => svc.GetAsync(invoiceId, ct));
        invoices.MapGet("/{invoiceId:guid}/pdf", async (Guid invoiceId, string? size, InvoiceService svc, CancellationToken ct) =>
            {
                var paper = (size ?? "K80").ToUpperInvariant() switch
                {
                    "K80" => InvoicePaperSize.K80,
                    "A5" => InvoicePaperSize.A5,
                    _ => throw ValidationFailedException.For("size", "Khổ giấy hỗ trợ: K80, A5."),
                };
                var (content, fileName) = await svc.RenderPdfAsync(invoiceId, paper, ct);
                return Results.File(content, "application/pdf", fileName);
            })
            .WithSummary("PDF hóa đơn nội bộ (K80 hoặc A5), font tiếng Việt nhúng. Không phải hóa đơn điện tử hợp lệ.");

        // ----- Reports & dashboard -----
        var reports = app.MapGroup("/reports").WithTags("Reports").RequireAuthorization(Policies.BranchAdmin);
        reports.MapGet("/revenue", ([AsParameters] ReportRange range, ReportService svc, CancellationToken ct) => svc.RevenueAsync(range, ct));
        reports.MapGet("/revenue/export.csv", async ([AsParameters] ReportRange range, ReportService svc, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await svc.ExportRevenueCsvAsync(range, buffer, ct);
            return Results.File(buffer.ToArray(), "text/csv; charset=utf-8", $"revenue_{range.From:yyyyMMdd}_{range.To:yyyyMMdd}.csv");
        }).WithSummary("CSV UTF-8 BOM, chống CSV formula injection.");
        reports.MapGet("/inventory/export.csv", async ([AsParameters] ReportRange range, ReportService svc, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await svc.ExportInventoryCsvAsync(range, buffer, ct);
            return Results.File(buffer.ToArray(), "text/csv; charset=utf-8", $"inventory_{range.From:yyyyMMdd}_{range.To:yyyyMMdd}.csv");
        });
        app.MapGet("/reports/dashboard", ([AsParameters] ReportRange range, ReportService svc, CancellationToken ct) => svc.DashboardAsync(range, ct))
            .WithTags("Reports").RequireAuthorization(Policies.Staff);
        return app;
    }

    private static readonly OrderActionRequest Empty = new(null, null);
}
