using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Fnb.Domain.Identity;
using Fnb.Domain.Inventory;
using Fnb.Domain.Menu;
using Fnb.Domain.Orders;
using Fnb.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fnb.Application.Abstractions;

/// <summary>
/// Persistence abstraction over EF Core. Use cases depend on this interface rather than the concrete DbContext
/// (pragmatic Clean Architecture: EF Core is accepted as the unit-of-work/repository; see ADR-002).
/// </summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<BranchTable> BranchTables { get; }
    DbSet<User> Users { get; }
    DbSet<UserBranchAssignment> UserBranchAssignments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Employee> Employees { get; }
    DbSet<WorkShift> WorkShifts { get; }
    DbSet<EmployeeShiftAssignment> ShiftAssignments { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollLine> PayrollLines { get; }
    DbSet<MenuCategory> MenuCategories { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<MenuItemVariant> MenuItemVariants { get; }
    DbSet<AddOn> AddOns { get; }
    DbSet<MenuItemAddOn> MenuItemAddOns { get; }
    DbSet<BranchMenuItem> BranchMenuItems { get; }
    DbSet<BranchVariantPrice> BranchVariantPrices { get; }
    DbSet<RecipeLine> RecipeLines { get; }
    DbSet<Ingredient> Ingredients { get; }
    DbSet<BranchInventory> BranchInventories { get; }
    DbSet<InventoryDocument> InventoryDocuments { get; }
    DbSet<InventoryDocumentLine> InventoryDocumentLines { get; }
    DbSet<InventoryLedgerEntry> InventoryLedgerEntries { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderItemAddOn> OrderItemAddOns { get; }
    DbSet<OrderStatusHistory> OrderStatusHistory { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface ISequenceAllocator
{
    /// <summary>Atomically allocates the next number for (scope, branch, businessDate). Must run inside the caller's transaction.</summary>
    Task<int> NextAsync(string scope, Guid branchId, DateOnly businessDate, CancellationToken ct);
}

public interface IUniqueConstraintDetector
{
    bool IsUniqueViolation(Exception exception);
}
