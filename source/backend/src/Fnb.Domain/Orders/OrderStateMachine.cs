namespace Fnb.Domain.Orders;

public enum OrderStatus
{
    Submitted = 1,
    PendingConfirmation = 2,
    Confirmed = 3,
    Preparing = 4,
    Ready = 5,
    Completed = 6,
    Cancelled = 7,
    Rejected = 8,
}

public enum OrderActor
{
    Customer = 1,
    Staff = 2,
    BranchAdmin = 3,
    System = 4,
}

/// <summary>
/// Explicit order state machine. Only transitions listed here are allowed; terminal states cannot be left.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Submitted] = [OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Rejected, OrderStatus.Cancelled],
        [OrderStatus.PendingConfirmation] = [OrderStatus.Confirmed, OrderStatus.Rejected, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Preparing, OrderStatus.Cancelled],
        [OrderStatus.Preparing] = [OrderStatus.Ready, OrderStatus.Cancelled],
        [OrderStatus.Ready] = [OrderStatus.Completed, OrderStatus.Cancelled],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Rejected] = [],
    };

    public static bool IsTerminal(OrderStatus status) => Allowed[status].Length == 0;

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public static IReadOnlyList<OrderStatus> NextStates(OrderStatus from) => Allowed[from];

    /// <summary>
    /// Role rules on top of the structural transitions:
    /// - System may only move Submitted → PendingConfirmation.
    /// - Customer may only cancel before confirmation.
    /// - Staff may cancel only before confirmation; after that only BranchAdmin (or above) may cancel.
    /// </summary>
    public static bool IsActorAllowed(OrderStatus from, OrderStatus to, OrderActor actor)
    {
        if (!CanTransition(from, to))
        {
            return false;
        }

        return actor switch
        {
            OrderActor.System => from == OrderStatus.Submitted && to == OrderStatus.PendingConfirmation,
            OrderActor.Customer => to == OrderStatus.Cancelled && from is OrderStatus.Submitted or OrderStatus.PendingConfirmation,
            OrderActor.Staff => to != OrderStatus.Cancelled || from is OrderStatus.Submitted or OrderStatus.PendingConfirmation,
            OrderActor.BranchAdmin => true,
            _ => false,
        };
    }

    /// <summary>Inventory is consumed when the order enters Confirmed.</summary>
    public static bool ConsumesInventory(OrderStatus to) => to == OrderStatus.Confirmed;
}
