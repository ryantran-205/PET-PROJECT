using Fnb.Application.Abstractions;
using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Fnb.Domain.Identity;
using Fnb.Domain.Inventory;
using Fnb.Domain.Menu;
using Fnb.Domain.Orders;
using Fnb.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Fnb.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BranchTable> BranchTables => Set<BranchTable>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserBranchAssignment> UserBranchAssignments => Set<UserBranchAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();
    public DbSet<EmployeeShiftAssignment> ShiftAssignments => Set<EmployeeShiftAssignment>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemVariant> MenuItemVariants => Set<MenuItemVariant>();
    public DbSet<AddOn> AddOns => Set<AddOn>();
    public DbSet<MenuItemAddOn> MenuItemAddOns => Set<MenuItemAddOn>();
    public DbSet<BranchMenuItem> BranchMenuItems => Set<BranchMenuItem>();
    public DbSet<BranchVariantPrice> BranchVariantPrices => Set<BranchVariantPrice>();
    public DbSet<RecipeLine> RecipeLines => Set<RecipeLine>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<BranchInventory> BranchInventories => Set<BranchInventory>();
    public DbSet<InventoryDocument> InventoryDocuments => Set<InventoryDocument>();
    public DbSet<InventoryDocumentLine> InventoryDocumentLines => Set<InventoryDocumentLine>();
    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemAddOn> OrderItemAddOns => Set<OrderItemAddOn>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money: decimal(18,2) by default. Ingredient quantities override to (18,3).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // All Guid keys are generated in code (Guid.CreateVersion7). Without ValueGeneratedNever, EF would treat a new
        // child entity discovered through a navigation (e.g. order.StatusHistory.Add) as an existing row → UPDATE → 0 rows.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var pk = entityType.FindPrimaryKey();
            if (pk is { Properties.Count: 1 } && pk.Properties[0].ClrType == typeof(Guid))
            {
                pk.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
