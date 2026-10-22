using Fnb.Domain.Common;
using Fnb.Domain.Hr;
using Fnb.Domain.Identity;
using Fnb.Domain.Inventory;
using Fnb.Domain.Menu;
using Fnb.Domain.Orders;
using Fnb.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fnb.Infrastructure.Persistence.Configurations;

internal static class Precision
{
    /// <summary>Ingredient quantities: 3 decimal places in base unit (g, ml, pcs).</summary>
    public static PropertyBuilder<decimal> Quantity(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 3);

    public static PropertyBuilder<decimal?> Quantity(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 3);
}

internal sealed class OrganizationConfig : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.BrandName).HasMaxLength(150);
        b.Property(x => x.Currency).HasMaxLength(3);
    }
}

internal sealed class BranchConfig : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.Property(x => x.Code).HasMaxLength(30);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.TimeZoneId).HasMaxLength(64);
        b.Property(x => x.InvoiceHeader).HasMaxLength(300);
        b.Property(x => x.InvoiceFooter).HasMaxLength(300);
        b.Property(x => x.TaxRatePercent).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Tables).WithOne().HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchTableConfig : IEntityTypeConfiguration<BranchTable>
{
    public void Configure(EntityTypeBuilder<BranchTable> b)
    {
        b.Property(x => x.Name).HasMaxLength(50);
        b.Property(x => x.PublicCode).HasMaxLength(50);
        b.HasIndex(x => x.PublicCode).IsUnique();
        b.HasIndex(x => x.BranchId);
    }
}

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.UserName).HasMaxLength(100);
        b.Property(x => x.NormalizedUserName).HasMaxLength(100);
        b.HasIndex(x => x.NormalizedUserName).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.SecurityStamp).HasMaxLength(64);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.BranchAssignments).WithOne().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserBranchAssignmentConfig : IEntityTypeConfiguration<UserBranchAssignment>
{
    public void Configure(EntityTypeBuilder<UserBranchAssignment> b)
    {
        b.HasKey(x => new { x.UserId, x.BranchId });
        b.HasIndex(x => x.BranchId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmployeeConfig : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.Property(x => x.EmployeeCode).HasMaxLength(30);
        b.HasIndex(x => x.EmployeeCode).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Position).HasMaxLength(50);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchId, x.IsActive });
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkShiftConfig : IEntityTypeConfiguration<WorkShift>
{
    public void Configure(EntityTypeBuilder<WorkShift> b)
    {
        b.Property(x => x.Name).HasMaxLength(50);
        b.Property(x => x.PayMultiplier).HasPrecision(5, 2);
        b.HasIndex(x => x.BranchId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShiftAssignmentConfig : IEntityTypeConfiguration<EmployeeShiftAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeShiftAssignment> b)
    {
        b.ToTable("EmployeeShiftAssignments");
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.WorkShiftId }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.WorkDate });
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WorkShift).WithMany().HasForeignKey(x => x.WorkShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaveRequestConfig : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ReviewNote).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasIndex(x => new { x.EmployeeId, x.StartDate });
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendanceRecordConfig : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.Property(x => x.CheckInIp).HasMaxLength(64);
        b.Property(x => x.CheckOutIp).HasMaxLength(64);
        b.Property(x => x.RoundingRule).HasMaxLength(32);
        b.Property(x => x.PayMultiplier).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchId, x.WorkDate });
        b.HasIndex(x => new { x.EmployeeId, x.WorkDate });
        // At most one open (not checked-out) record per employee — blocks concurrent double check-in.
        b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[CheckOutUtc] IS NULL").HasDatabaseName("UX_Attendance_OpenPerEmployee");
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayrollPeriodConfig : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> b)
    {
        b.Property(x => x.Formula).HasMaxLength(300);
        b.HasIndex(x => new { x.BranchId, x.PeriodStart, x.PeriodEnd }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PayrollPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayrollLineConfig : IEntityTypeConfiguration<PayrollLine>
{
    public void Configure(EntityTypeBuilder<PayrollLine> b)
    {
        b.Property(x => x.EmployeeCode).HasMaxLength(30);
        b.Property(x => x.EmployeeName).HasMaxLength(150);
        b.Property(x => x.WeightedHours).HasPrecision(18, 4);
    }
}

internal sealed class MenuCategoryConfig : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> b) => b.Property(x => x.Name).HasMaxLength(100);
}

internal sealed class MenuItemConfig : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        b.Property(x => x.Sku).HasMaxLength(40);
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.CategoryId);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.AddOns).WithOne().HasForeignKey(a => a.MenuItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MenuItemVariantConfig : IEntityTypeConfiguration<MenuItemVariant>
{
    public void Configure(EntityTypeBuilder<MenuItemVariant> b) => b.Property(x => x.Name).HasMaxLength(30);
}

internal sealed class AddOnConfig : IEntityTypeConfiguration<AddOn>
{
    public void Configure(EntityTypeBuilder<AddOn> b) => b.Property(x => x.Name).HasMaxLength(100);
}

internal sealed class MenuItemAddOnConfig : IEntityTypeConfiguration<MenuItemAddOn>
{
    public void Configure(EntityTypeBuilder<MenuItemAddOn> b)
    {
        b.HasKey(x => new { x.MenuItemId, x.AddOnId });
        b.HasOne(x => x.AddOn).WithMany().HasForeignKey(x => x.AddOnId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchMenuItemConfig : IEntityTypeConfiguration<BranchMenuItem>
{
    public void Configure(EntityTypeBuilder<BranchMenuItem> b)
    {
        b.HasKey(x => new { x.BranchId, x.MenuItemId });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MenuItem>().WithMany().HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchVariantPriceConfig : IEntityTypeConfiguration<BranchVariantPrice>
{
    public void Configure(EntityTypeBuilder<BranchVariantPrice> b)
    {
        b.HasKey(x => new { x.BranchId, x.VariantId });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MenuItemVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecipeLineConfig : IEntityTypeConfiguration<RecipeLine>
{
    public void Configure(EntityTypeBuilder<RecipeLine> b)
    {
        b.Property(x => x.Quantity).Quantity();
        b.HasIndex(x => x.VariantId);
        b.HasIndex(x => x.AddOnId);
        b.ToTable(t => t.HasCheckConstraint("CK_RecipeLine_Owner",
            "([VariantId] IS NOT NULL AND [AddOnId] IS NULL) OR ([VariantId] IS NULL AND [AddOnId] IS NOT NULL)"));
        b.HasOne<MenuItemVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AddOn>().WithMany().HasForeignKey(x => x.AddOnId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IngredientConfig : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> b)
    {
        b.Property(x => x.Code).HasMaxLength(30);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Unit).HasMaxLength(10);
    }
}

internal sealed class BranchInventoryConfig : IEntityTypeConfiguration<BranchInventory>
{
    public void Configure(EntityTypeBuilder<BranchInventory> b)
    {
        b.HasKey(x => new { x.BranchId, x.IngredientId });
        b.Property(x => x.QuantityOnHand).Quantity();
        b.Property(x => x.MinThreshold).Quantity();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.IsLow);
        b.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryDocumentConfig : IEntityTypeConfiguration<InventoryDocument>
{
    public void Configure(EntityTypeBuilder<InventoryDocument> b)
    {
        b.Property(x => x.DocumentNo).HasMaxLength(50);
        b.HasIndex(x => x.DocumentNo).IsUnique();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.Supplier).HasMaxLength(200);
        b.HasIndex(x => new { x.BranchId, x.CreatedAtUtc });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryDocumentLineConfig : IEntityTypeConfiguration<InventoryDocumentLine>
{
    public void Configure(EntityTypeBuilder<InventoryDocumentLine> b)
    {
        b.Property(x => x.QuantityChange).Quantity();
        b.Property(x => x.CountedQuantity).Quantity();
        b.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryLedgerEntryConfig : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> b)
    {
        b.Property(x => x.QuantityChange).Quantity();
        b.Property(x => x.BalanceAfter).Quantity();
        b.Property(x => x.SourceType).HasMaxLength(40);
        b.Property(x => x.ReferenceKey).HasMaxLength(150);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => x.ReferenceKey).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.IngredientId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.BranchId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.SourceType, x.SourceId });
        b.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderConfig : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.PublicTrackingTokenHash).HasMaxLength(64);
        b.HasIndex(x => x.PublicTrackingTokenHash).IsUnique();
        b.Property(x => x.TableName).HasMaxLength(50);
        b.Property(x => x.CustomerNote).HasMaxLength(500);
        b.Property(x => x.AttentionNote).HasMaxLength(500);
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.PricingRuleSnapshot).HasMaxLength(300);
        b.Property(x => x.TaxRatePercent).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchId, x.BusinessDate, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status, x.CreatedAtUtc });
        b.HasIndex(x => new { x.BranchId, x.CreatedAtUtc });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemConfig : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.Property(x => x.ItemName).HasMaxLength(150);
        b.Property(x => x.VariantName).HasMaxLength(30);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => x.MenuItemId);
        b.HasMany(x => x.AddOns).WithOne().HasForeignKey(a => a.OrderItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemAddOnConfig : IEntityTypeConfiguration<OrderItemAddOn>
{
    public void Configure(EntityTypeBuilder<OrderItemAddOn> b) => b.Property(x => x.Name).HasMaxLength(100);
}

internal sealed class OrderStatusHistoryConfig : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("OrderStatusHistory");
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => new { x.OrderId, x.ChangedAtUtc });
    }
}

internal sealed class NumberSequenceConfig : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.HasKey(x => new { x.Scope, x.BranchId, x.BusinessDate });
        b.Property(x => x.Scope).HasMaxLength(40);
    }
}

internal sealed class InvoiceConfig : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.Property(x => x.InvoiceNo).HasMaxLength(50);
        b.HasIndex(x => x.InvoiceNo).IsUnique();
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.BusinessDate });
        b.HasIndex(x => new { x.BranchId, x.PaymentStatus, x.BusinessDate });
        b.Property(x => x.BrandName).HasMaxLength(300);
        b.Property(x => x.BranchName).HasMaxLength(150);
        b.Property(x => x.BranchAddress).HasMaxLength(300);
        b.Property(x => x.BranchPhone).HasMaxLength(30);
        b.Property(x => x.Footer).HasMaxLength(300);
        b.Property(x => x.PricingRuleSnapshot).HasMaxLength(300);
        b.Property(x => x.TaxRatePercent).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceLineConfig : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.Property(x => x.ItemName).HasMaxLength(150);
        b.Property(x => x.VariantName).HasMaxLength(30);
        b.Property(x => x.AddOnsText).HasMaxLength(500);
        b.Property(x => x.Note).HasMaxLength(300);
    }
}

internal sealed class PaymentConfig : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => new { x.BranchId, x.RecordedAtUtc });
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfig : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(150);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.ResponseJson).HasMaxLength(4000);
        b.HasIndex(x => x.ExpiresAtUtc);
    }
}

internal sealed class OutboxMessageConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.Property(x => x.Type).HasMaxLength(100);
        b.Property(x => x.Topic).HasMaxLength(100);
        b.Property(x => x.Payload).HasMaxLength(4000);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasIndex(x => new { x.PublishedAtUtc, x.Failed, x.NextAttemptAtUtc });
    }
}

internal sealed class ProcessedMessageConfig : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> b)
    {
        b.HasKey(x => new { x.MessageId, x.Consumer });
        b.Property(x => x.Consumer).HasMaxLength(100);
        b.HasIndex(x => x.ProcessedAtUtc);
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.ActorName).HasMaxLength(100);
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.ResourceType).HasMaxLength(100);
        b.Property(x => x.ResourceId).HasMaxLength(100);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.Changes).HasColumnType("nvarchar(max)");
        b.HasIndex(x => x.TimestampUtc);
        b.HasIndex(x => new { x.BranchId, x.TimestampUtc });
        b.HasIndex(x => new { x.Action, x.TimestampUtc });
    }
}
