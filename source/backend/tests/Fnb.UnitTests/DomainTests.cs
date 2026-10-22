using Fnb.Application.Reports;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Fnb.Domain.Inventory;
using Fnb.Domain.Orders;
using Fnb.Infrastructure.Messaging;

namespace Fnb.UnitTests;

public sealed class PricingCalculatorTests
{
    [Fact]
    public void Subtotal_includes_addons_times_quantity()
    {
        var result = PricingCalculator.Calculate([new PricingLine(30000, 8000, 2), new PricingLine(25000, 0, 1)], 0, PricingPolicy.NoTax);
        Assert.Equal(101000, result.Subtotal);
        Assert.Equal(101000, result.TotalAmount);
        Assert.Equal(0, result.TaxAmount);
    }

    [Fact]
    public void Exclusive_tax_is_added_after_discount()
    {
        var result = PricingCalculator.Calculate([new PricingLine(100000, 0, 1)], 10000, new PricingPolicy(8, false, 1));
        Assert.Equal(10000, result.DiscountAmount);
        Assert.Equal(7200, result.TaxAmount);
        Assert.Equal(97200, result.TotalAmount);
    }

    [Fact]
    public void Inclusive_tax_is_extracted_not_added()
    {
        var result = PricingCalculator.Calculate([new PricingLine(108000, 0, 1)], 0, new PricingPolicy(8, true, 1));
        Assert.Equal(8000, result.TaxAmount);
        Assert.Equal(108000, result.TotalAmount);
    }

    [Fact]
    public void Discount_is_capped_at_subtotal()
    {
        var result = PricingCalculator.Calculate([new PricingLine(20000, 0, 1)], 50000, PricingPolicy.NoTax);
        Assert.Equal(20000, result.DiscountAmount);
        Assert.Equal(0, result.TotalAmount);
    }

    [Theory]
    [InlineData(101400, 1000, 101000, -400)]
    [InlineData(101500, 1000, 102000, 500)]
    [InlineData(101499, 500, 101500, 1)]
    public void Total_is_rounded_to_unit(decimal price, int unit, decimal expectedTotal, decimal expectedRounding)
    {
        var result = PricingCalculator.Calculate([new PricingLine(price, 0, 1)], 0, new PricingPolicy(0, true, unit));
        Assert.Equal(expectedTotal, result.TotalAmount);
        Assert.Equal(expectedRounding, result.RoundingAmount);
    }

    [Fact]
    public void Negative_discount_or_invalid_tax_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PricingCalculator.Calculate([], -1, PricingPolicy.NoTax));
        Assert.Throws<ArgumentOutOfRangeException>(() => PricingCalculator.Calculate([], 0, new PricingPolicy(101, true, 1)));
    }
}

public sealed class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.Submitted, OrderStatus.PendingConfirmation, true)]
    [InlineData(OrderStatus.PendingConfirmation, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Preparing, true)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Ready, true)]
    [InlineData(OrderStatus.Ready, OrderStatus.Completed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Completed, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Rejected, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Ready, OrderStatus.Preparing, false)]
    public void Structural_transitions(OrderStatus from, OrderStatus to, bool expected) =>
        Assert.Equal(expected, OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void Terminal_states_have_no_exit()
    {
        Assert.True(OrderStateMachine.IsTerminal(OrderStatus.Completed));
        Assert.True(OrderStateMachine.IsTerminal(OrderStatus.Cancelled));
        Assert.True(OrderStateMachine.IsTerminal(OrderStatus.Rejected));
        Assert.False(OrderStateMachine.IsTerminal(OrderStatus.Ready));
    }

    [Fact]
    public void Customer_may_cancel_only_before_confirmation()
    {
        Assert.True(OrderStateMachine.IsActorAllowed(OrderStatus.PendingConfirmation, OrderStatus.Cancelled, OrderActor.Customer));
        Assert.False(OrderStateMachine.IsActorAllowed(OrderStatus.Confirmed, OrderStatus.Cancelled, OrderActor.Customer));
        Assert.False(OrderStateMachine.IsActorAllowed(OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderActor.Customer));
    }

    [Fact]
    public void Staff_cannot_cancel_after_confirmation_but_admin_can()
    {
        Assert.False(OrderStateMachine.IsActorAllowed(OrderStatus.Preparing, OrderStatus.Cancelled, OrderActor.Staff));
        Assert.True(OrderStateMachine.IsActorAllowed(OrderStatus.Preparing, OrderStatus.Cancelled, OrderActor.BranchAdmin));
        Assert.True(OrderStateMachine.IsActorAllowed(OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderActor.Staff));
    }

    [Fact]
    public void System_only_moves_submitted_to_pending()
    {
        Assert.True(OrderStateMachine.IsActorAllowed(OrderStatus.Submitted, OrderStatus.PendingConfirmation, OrderActor.System));
        Assert.False(OrderStateMachine.IsActorAllowed(OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderActor.System));
    }

    [Fact]
    public void Order_transition_records_history_and_requires_cancel_reason()
    {
        var order = new Order { PublicTrackingTokenHash = "x", PricingRuleSnapshot = "p" };
        var now = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        order.TransitionTo(OrderStatus.PendingConfirmation, OrderActor.System, null, null, now);
        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);
        Assert.Single(order.StatusHistory);

        Assert.ThrowsAny<AppException>(() => order.TransitionTo(OrderStatus.Cancelled, OrderActor.Staff, Guid.NewGuid(), null, now));
        Assert.ThrowsAny<AppException>(() => order.TransitionTo(OrderStatus.Completed, OrderActor.BranchAdmin, Guid.NewGuid(), null, now));
    }

    [Fact]
    public void Payment_transitions()
    {
        Assert.True(PaymentStateMachine.CanTransition(PaymentStatus.Unpaid, PaymentStatus.Paid));
        Assert.True(PaymentStateMachine.CanTransition(PaymentStatus.Paid, PaymentStatus.Refunded));
        Assert.False(PaymentStateMachine.CanTransition(PaymentStatus.Refunded, PaymentStatus.Paid));
        Assert.Throws<BusinessRuleException>(() => PaymentStateMachine.EnsureTransition(PaymentStatus.Unpaid, PaymentStatus.Refunded));
    }
}

public sealed class AttendanceTests
{
    [Theory]
    [InlineData(RoundingMode.Floor, 15, 128, 120)]
    [InlineData(RoundingMode.Ceiling, 15, 121, 135)]
    [InlineData(RoundingMode.Nearest, 15, 127, 120)]
    [InlineData(RoundingMode.Nearest, 15, 128, 135)]
    [InlineData(RoundingMode.Floor, 1, 128, 128)]
    [InlineData(RoundingMode.Floor, 15, 0, 0)]
    public void Rounding_policy(RoundingMode mode, int increment, int worked, int expected) =>
        Assert.Equal(expected, new AttendanceRoundingPolicy(increment, mode).Apply(worked));

    [Fact]
    public void Overnight_shift_worked_minutes_cross_midnight()
    {
        // 22:00 → 06:15 local (UTC+7) = 15:00Z → 23:15Z
        var minutes = AttendanceCalculator.WorkedMinutes(new DateTime(2026, 3, 1, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 23, 15, 0, DateTimeKind.Utc));
        Assert.Equal(495, minutes);
    }

    [Fact]
    public void Overnight_shift_window_ends_next_day()
    {
        var shift = new WorkShift { Name = "Đêm", StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0) };
        Assert.True(shift.CrossesMidnight);
        var (start, end) = shift.WindowOn(new DateOnly(2026, 3, 1));
        Assert.Equal(new DateTime(2026, 3, 1, 22, 0, 0), start);
        Assert.Equal(new DateTime(2026, 3, 2, 6, 0, 0), end);
    }

    [Fact]
    public void Shift_overlap_detection()
    {
        var a = (new DateTime(2026, 3, 1, 6, 0, 0), new DateTime(2026, 3, 1, 14, 0, 0));
        var b = (new DateTime(2026, 3, 1, 14, 0, 0), new DateTime(2026, 3, 1, 22, 0, 0));
        var c = (new DateTime(2026, 3, 1, 13, 0, 0), new DateTime(2026, 3, 1, 15, 0, 0));
        Assert.False(AttendanceCalculator.Overlaps(a, b));
        Assert.True(AttendanceCalculator.Overlaps(a, c));
    }

    [Fact]
    public void Basic_pay_formula()
    {
        var hours = AttendanceCalculator.WeightedHours(480, 1.3m);
        Assert.Equal(10.4m, hours);
        Assert.Equal(260000, AttendanceCalculator.BasicPay(hours, 25000));
    }

    [Fact]
    public void Checkout_before_checkin_is_rejected()
    {
        var record = new AttendanceRecord { CheckInUtc = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc) };
        Assert.Throws<BusinessRuleException>(() =>
            record.CheckOut(new DateTime(2026, 3, 1, 7, 0, 0, DateTimeKind.Utc), AttendanceSource.Web, null, AttendanceRoundingPolicy.Default));
    }

    [Fact]
    public void Checkout_computes_payable_minutes()
    {
        var record = new AttendanceRecord { CheckInUtc = new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc) };
        record.CheckOut(new DateTime(2026, 3, 1, 9, 7, 0, DateTimeKind.Utc), AttendanceSource.Web, null, AttendanceRoundingPolicy.Default);
        Assert.Equal(487, record.WorkedMinutes);
        Assert.Equal(480, record.PayableMinutes);
        Assert.Throws<BusinessRuleException>(() =>
            record.CheckOut(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), AttendanceSource.Web, null, AttendanceRoundingPolicy.Default));
    }
}

public sealed class RecipeCalculatorTests
{
    private static readonly Guid Variant = Guid.NewGuid();
    private static readonly Guid AddOn = Guid.NewGuid();
    private static readonly Guid Coffee = Guid.NewGuid();
    private static readonly Guid Pearl = Guid.NewGuid();

    [Fact]
    public void Requirements_aggregate_variant_and_addons()
    {
        var req = RecipeCalculator.Requirements(
            [new ConsumptionLine(Variant, [AddOn], 2), new ConsumptionLine(Variant, [], 1)],
            new Dictionary<Guid, IReadOnlyList<RecipeComponent>> { [Variant] = [new RecipeComponent(Coffee, 18)] },
            new Dictionary<Guid, IReadOnlyList<RecipeComponent>> { [AddOn] = [new RecipeComponent(Pearl, 30), new RecipeComponent(Coffee, 9)] });
        Assert.Equal(18 * 3 + 9 * 2, req[Coffee]);
        Assert.Equal(60, req[Pearl]);
    }

    [Fact]
    public void Shortfalls_only_for_insufficient_stock()
    {
        var shortfalls = RecipeCalculator.Shortfalls(
            new Dictionary<Guid, decimal> { [Coffee] = 100, [Pearl] = 10 },
            new Dictionary<Guid, decimal> { [Coffee] = 40, [Pearl] = 10 });
        Assert.Single(shortfalls);
        Assert.Equal(60, shortfalls[Coffee]);
    }
}

public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1", "'+1")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@x", "'@x")]
    [InlineData("Cà phê sữa", "Cà phê sữa")]
    public void Formula_injection_is_neutralised(string input, string expected) =>
        Assert.Equal(expected, CsvWriter.FormatCell(input));

    [Fact]
    public void Quotes_commas_and_newlines_are_escaped()
    {
        Assert.Equal("\"a,b\"", CsvWriter.FormatCell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.FormatCell("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", CsvWriter.FormatCell("line1\nline2"));
    }

    [Fact]
    public void Numbers_use_invariant_culture()
    {
        Assert.Equal("1234.5", CsvWriter.FormatCell(1234.5m));
        Assert.Equal("-50000", CsvWriter.FormatCell(-50000m));
        Assert.Equal("2026-03-01", CsvWriter.FormatCell(new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public async Task Output_starts_with_utf8_bom()
    {
        using var ms = new MemoryStream();
        await using (var writer = new CsvWriter(ms))
        {
            await writer.WriteRowAsync("Món", 1);
        }

        var bytes = ms.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("Món,1\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }
}

public sealed class FailurePolicyTests
{
    [Fact]
    public void Permanent_errors_dead_letter_immediately()
    {
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new Fnb.Application.Abstractions.PermanentMessageException("bad"), 1, 5));
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new System.Text.Json.JsonException(), 1, 5));
    }

    [Fact]
    public void Transient_errors_retry_until_limit()
    {
        Assert.Equal(FailureAction.Retry, FailurePolicy.Decide(new TimeoutException(), 4, 5));
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new TimeoutException(), 5, 5));
    }

    [Fact]
    public void Retry_delay_grows_and_is_capped()
    {
        var opt = new MessagingOptions { BaseRetryDelayMs = 100, MaxRetryDelayMs = 1000 };
        Assert.InRange(opt.RetryDelay(1).TotalMilliseconds, 100, 350);
        Assert.InRange(opt.RetryDelay(3).TotalMilliseconds, 400, 650);
        Assert.InRange(opt.RetryDelay(20).TotalMilliseconds, 1000, 1250);
    }
}
