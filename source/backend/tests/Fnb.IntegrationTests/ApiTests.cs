using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fnb.Application.Orders;
using Fnb.Contracts;
using Fnb.Domain.Orders;
using Fnb.Infrastructure.Messaging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fnb.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthAndScopeTests(FnbApiFactory factory)
{
    [Fact]
    public async Task Login_with_wrong_password_returns_401_problem_details()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "staff.q1", password = "wrong-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("unauthorized", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Anonymous_request_to_admin_api_returns_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_create_branch_403()
    {
        var client = await factory.LoginAsync("staff.q1");
        var response = await client.PostAsJsonAsync("/api/v1/branches",
            new { code = "X-1", name = "X", address = "Y", taxRatePercent = 0, pricesIncludeTax = true, roundingUnit = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_read_other_branch_orders_403()
    {
        var q3 = await factory.BranchIdAsync("HCM-Q3");
        var client = await factory.LoginAsync("staff.q1");
        var response = await client.GetAsync($"/api/v1/branches/{q3}/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Branch_list_is_scoped_to_assignment()
    {
        var client = await factory.LoginAsync("admin.q1");
        var branches = await client.GetFromJsonAsync<JsonElement>("/api/v1/branches", TestContext.Current.CancellationToken);
        Assert.Equal(1, branches.GetArrayLength());
        Assert.Equal("HCM-Q1", branches[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_token_rotation_detects_reuse()
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin.q3", password = FnbApiFactory.Password }, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var refresh1 = login.GetProperty("refreshToken").GetString();

        var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var refresh2 = (await rotated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("refreshToken").GetString();

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // Reuse revokes the whole family: the newest token must no longer work either.
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh2 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_are_available()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class OrderFlowTests(FnbApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object> BuildOrderBodyAsync(HttpClient client, string branchCode, int quantity = 1)
    {
        var menu = await client.GetFromJsonAsync<JsonElement>($"/api/v1/public/branches/{branchCode}/menu", Ct);
        var item = menu.GetProperty("items").EnumerateArray().First(i => i.GetProperty("name").GetString() == "Cà phê sữa");
        var variant = item.GetProperty("variants")[0];
        return new
        {
            branchCode,
            channel = "Counter",
            items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), variantId = variant.GetProperty("id").GetGuid(), quantity, addOnIds = Array.Empty<Guid>(), note = "Ít đá" } },
        };
    }

    private static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, object body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/orders") { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<JsonElement> WaitForStatusAsync(HttpClient staff, Guid orderId, string status)
    {
        for (var i = 0; i < 100; i++)
        {
            var order = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}", Ct);
            if (order.GetProperty("status").GetString() == status)
            {
                return order;
            }

            await Task.Delay(100, Ct);
        }

        throw new TimeoutException($"Order {orderId} did not reach {status}.");
    }

    [Fact]
    public async Task Public_order_requires_idempotency_key()
    {
        var client = factory.CreateClient();
        var response = await PostOrderAsync(client, await BuildOrderBodyAsync(client, "HCM-Q1"), null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Idempotent_replay_returns_same_order_and_conflict_on_different_payload()
    {
        var client = factory.CreateClient();
        var body = await BuildOrderBodyAsync(client, "HCM-Q1");
        var key = Guid.NewGuid().ToString("N");

        var first = await PostOrderAsync(client, body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstOrder = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var replay = await PostOrderAsync(client, body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        var replayOrder = await replay.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(firstOrder.GetProperty("orderId").GetGuid(), replayOrder.GetProperty("orderId").GetGuid());
        Assert.Equal(firstOrder.GetProperty("orderNumber").GetInt32(), replayOrder.GetProperty("orderNumber").GetInt32());

        var different = await PostOrderAsync(client, await BuildOrderBodyAsync(client, "HCM-Q1", 3), key);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
    }

    [Fact]
    public async Task Price_mismatch_returns_409_price_changed()
    {
        var client = factory.CreateClient();
        var menu = await client.GetFromJsonAsync<JsonElement>("/api/v1/public/branches/HCM-Q1/menu", Ct);
        var item = menu.GetProperty("items")[0];
        var body = new
        {
            branchCode = "HCM-Q1",
            items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), variantId = item.GetProperty("variants")[0].GetProperty("id").GetGuid(), quantity = 1 } },
            expectedTotal = 1m,
        };
        var response = await PostOrderAsync(client, body, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("price_changed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_orders_get_unique_sequential_numbers()
    {
        var client = factory.CreateClient();
        var body = await BuildOrderBodyAsync(client, "HCM-Q3");
        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => PostOrderAsync(client, body, Guid.NewGuid().ToString("N"))));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var numbers = new List<int>();
        foreach (var r in responses)
        {
            numbers.Add((await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("orderNumber").GetInt32());
        }

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        var dbNumbers = await factory.WithDbAsync(async db =>
        {
            var q3 = await db.Branches.Where(b => b.Code == "HCM-Q3").Select(b => b.Id).SingleAsync(Ct);
            return await db.Orders.Where(o => o.BranchId == q3).Select(o => o.OrderNumber).ToListAsync(Ct);
        });
        Assert.Equal(dbNumbers.Count, dbNumbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, dbNumbers.Count), dbNumbers.Order());
    }

    [Fact]
    public async Task Full_flow_confirm_consumes_stock_once_cancel_reverses_and_invoice_pdf_works()
    {
        var anon = factory.CreateClient();
        var created = await PostOrderAsync(anon, await BuildOrderBodyAsync(anon, "HCM-Q1", 2), Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var orderId = order.GetProperty("orderId").GetGuid();
        var token = order.GetProperty("trackingToken").GetString()!;

        // Public tracking works with the token only.
        var tracked = await anon.GetFromJsonAsync<JsonElement>($"/api/v1/public/orders/{token}", Ct);
        Assert.Equal(orderId, tracked.GetProperty("orderId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/v1/public/orders/not-a-real-token-123", Ct)).StatusCode);

        var staff = await factory.LoginAsync("staff.q1");
        await WaitForStatusAsync(staff, orderId, "PendingConfirmation");

        var q1 = await factory.BranchIdAsync("HCM-Q1");
        decimal CoffeeOnHand(JsonElement stock) => stock.EnumerateArray().First(s => s.GetProperty("code").GetString() == "CF-BEAN").GetProperty("quantityOnHand").GetDecimal();
        var before = CoffeeOnHand(await staff.GetFromJsonAsync<JsonElement>($"/api/v1/branches/{q1}/inventory", Ct));

        var confirm = await staff.PostAsJsonAsync($"/api/v1/orders/{orderId}/confirm", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var afterConfirm = CoffeeOnHand(await staff.GetFromJsonAsync<JsonElement>($"/api/v1/branches/{q1}/inventory", Ct));
        Assert.Equal(before - (18 * 2), afterConfirm);

        // Second confirm is an invalid transition and must not consume again.
        var again = await staff.PostAsJsonAsync($"/api/v1/orders/{orderId}/confirm", new { }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(afterConfirm, CoffeeOnHand(await staff.GetFromJsonAsync<JsonElement>($"/api/v1/branches/{q1}/inventory", Ct)));

        // Customer can no longer cancel after confirmation.
        var customerCancel = await anon.PostAsJsonAsync($"/api/v1/public/orders/{token}/cancel", new { reason = "đổi ý" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, customerCancel.StatusCode);

        // Payment → invoice → PDF.
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}", Ct);
        var pay = await staff.PostAsJsonAsync($"/api/v1/orders/{orderId}/payments",
            new { method = "Cash", amount = detail.GetProperty("totalAmount").GetDecimal(), amountTendered = 200000m }, Ct);
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        var invoice = await (await staff.PostAsync($"/api/v1/orders/{orderId}/invoice", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var invoiceId = invoice.GetProperty("id").GetGuid();
        var pdf = await staff.GetAsync($"/api/v1/invoices/{invoiceId}/pdf?size=K80", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        // Staff may not cancel after confirmation; branch admin can, and stock is restored via reversal entries.
        var staffCancel = await staff.PostAsJsonAsync($"/api/v1/orders/{orderId}/cancel", new { reason = "khách đổi ý" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffCancel.StatusCode);
    }

    [Fact]
    public async Task Admin_cancel_after_confirm_reverses_inventory_exactly_once()
    {
        var anon = factory.CreateClient();
        var created = await PostOrderAsync(anon, await BuildOrderBodyAsync(anon, "HCM-Q1", 1), Guid.NewGuid().ToString("N"));
        var orderId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("orderId").GetGuid();
        var admin = await factory.LoginAsync("admin.q1");
        await WaitForStatusAsync(admin, orderId, "PendingConfirmation");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/confirm", new { }, Ct)).StatusCode);

        var cancel = await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/cancel", new { reason = "hết nguyên liệu" }, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelNoReason = await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/cancel", new { }, Ct);
        Assert.True(cancelNoReason.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);

        var (consumed, reversed) = await factory.WithDbAsync(async db =>
        {
            var entries = await db.InventoryLedgerEntries.Where(e => e.SourceId == orderId).ToListAsync(Ct);
            return (entries.Where(e => e.EntryType == Domain.Inventory.LedgerEntryType.OrderConsumption).Sum(e => e.QuantityChange),
                entries.Where(e => e.EntryType == Domain.Inventory.LedgerEntryType.OrderReversal).Sum(e => e.QuantityChange));
        });
        Assert.True(consumed < 0);
        Assert.Equal(-consumed, reversed);
    }

    [Fact]
    public async Task Duplicate_message_is_processed_once()
    {
        var anon = factory.CreateClient();
        var created = await PostOrderAsync(anon, await BuildOrderBodyAsync(anon, "HCM-Q3"), Guid.NewGuid().ToString("N"));
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var orderId = dto.GetProperty("orderId").GetGuid();
        var admin = await factory.LoginAsync("admin.q3");
        await WaitForStatusAsync(admin, orderId, "PendingConfirmation");

        var order = await factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct));
        var envelope = new MessageEnvelope(Guid.NewGuid(), MessageTypes.OrderStatusChanged, "test-correlation", DateTime.UtcNow, 1,
            JsonSerializer.Serialize(new OrderStatusChangedV1(orderId, order.BranchId, order.OrderNumber, order.BusinessDate, "PendingConfirmation", "Confirmed", DateTime.UtcNow), MessagingJson.Options));

        // Confirm via API first (creates the real event), then deliver a synthetic duplicate twice.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/confirm", new { }, Ct)).StatusCode);
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentUserContext>().Override = Application.Abstractions.SystemUser.Instance;
            await scope.ServiceProvider.GetRequiredService<OrderProcessingHandler>().HandleAsync(envelope, Ct);
        }

        var (invoices, processed) = await factory.WithDbAsync(async db =>
            (await db.Invoices.CountAsync(x => x.OrderId == orderId, Ct),
             await db.ProcessedMessages.CountAsync(p => p.MessageId == envelope.MessageId, Ct)));
        Assert.Equal(1, invoices);
        Assert.Equal(1, processed);
    }

    [Fact]
    public async Task Revenue_csv_export_has_bom_and_requires_admin()
    {
        var q1 = await factory.BranchIdAsync("HCM-Q1");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var url = $"/api/v1/reports/revenue/export.csv?branchId={q1}&from={today.AddDays(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}";

        var staff = await factory.LoginAsync("staff.q1");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url, Ct)).StatusCode);

        var admin = await factory.LoginAsync("admin.q1");
        var response = await admin.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public async Task Revenue_dashboard_and_queue_reflect_paid_order()
    {
        var anon = factory.CreateClient();
        var created = await PostOrderAsync(anon, await BuildOrderBodyAsync(anon, "HCM-Q1"), Guid.NewGuid().ToString("N"));
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var orderId = dto.GetProperty("orderId").GetGuid();
        var orderNumber = dto.GetProperty("orderNumber").GetInt32();

        var queue = await anon.GetFromJsonAsync<JsonElement>("/api/v1/public/branches/hcm-q1/queue", Ct);
        Assert.Contains(queue.GetProperty("orders").EnumerateArray(), o => o.GetProperty("orderNumber").GetInt32() == orderNumber);
        Assert.False(queue.GetProperty("orders")[0].TryGetProperty("totalAmount", out _)); // queue exposes number + status only

        var admin = await factory.LoginAsync("admin.q1");
        await WaitForStatusAsync(admin, orderId, "PendingConfirmation");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/confirm", new { }, Ct)).StatusCode);
        var total = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}", Ct)).GetProperty("totalAmount").GetDecimal();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/payments", new { method = "BankTransferQr", amount = total, reference = "FT123" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/orders/{orderId}/invoice", null, Ct)).StatusCode);

        var q1 = await factory.BranchIdAsync("HCM-Q1");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var range = $"branchId={q1}&from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";

        var revenueResponse = await admin.GetAsync($"/api/v1/reports/revenue?{range}", Ct);
        Assert.Equal(HttpStatusCode.OK, revenueResponse.StatusCode);
        var revenue = await revenueResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(revenue.GetProperty("paidRevenue").GetDecimal() >= total);
        Assert.NotEmpty(revenue.GetProperty("byDay").EnumerateArray());
        Assert.Contains(revenue.GetProperty("byMethod").EnumerateArray(), m => m.GetProperty("method").GetString() == "BankTransferQr");

        var staff = await factory.LoginAsync("staff.q1");
        var dashResponse = await staff.GetAsync($"/api/v1/reports/dashboard?{range}", Ct);
        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var dash = await dashResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(dash.GetProperty("totalOrders").GetInt32() >= 1);
        Assert.NotEmpty(dash.GetProperty("topItems").EnumerateArray());
    }
}

[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(FnbApiFactory factory)
{
    private HubConnection Connect(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/orders"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    o.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();

    private async Task<string> TokenAsync(string user)
    {
        var client = await factory.LoginAsync(user);
        return client.DefaultRequestHeaders.Authorization!.Parameter!;
    }

    [Fact]
    public async Task Staff_cannot_join_other_branch_group()
    {
        var q3 = await factory.BranchIdAsync("HCM-Q3");
        await using var connection = Connect(await TokenAsync("staff.q1"));
        await connection.StartAsync(TestContext.Current.CancellationToken);
        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinBranchStaff", q3, TestContext.Current.CancellationToken));
        Assert.Contains("forbidden", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_cannot_join_staff_group()
    {
        var q1 = await factory.BranchIdAsync("HCM-Q1");
        await using var connection = Connect(null);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinBranchStaff", q1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Staff_receives_new_order_event()
    {
        var q1 = await factory.BranchIdAsync("HCM-Q1");
        await using var connection = Connect(await TokenAsync("staff.q1"));
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("orderCreated", e => received.TrySetResult(e));
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync("JoinBranchStaff", q1, TestContext.Current.CancellationToken);

        var anon = factory.CreateClient();
        var menu = await anon.GetFromJsonAsync<JsonElement>("/api/v1/public/branches/HCM-Q1/menu", TestContext.Current.CancellationToken);
        var item = menu.GetProperty("items")[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/orders")
        {
            Content = JsonContent.Create(new
            {
                branchCode = "HCM-Q1",
                items = new[] { new { menuItemId = item.GetProperty("id").GetGuid(), variantId = item.GetProperty("variants")[0].GetProperty("id").GetGuid(), quantity = 1 } },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var created = await anon.SendAsync(request, TestContext.Current.CancellationToken);
        var orderId = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("orderId").GetGuid();

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(orderId, evt.GetProperty("orderId").GetGuid());
    }
}
