namespace Fnb.Domain.Common;

/// <summary>Base type for all persisted entities. GUID primary keys are used consistently.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Marker for entities whose data is partitioned by branch.</summary>
public interface IBranchScoped
{
    Guid BranchId { get; }
}

/// <summary>Entities carrying creation/update timestamps (UTC).</summary>
public interface IHasTimestamps
{
    DateTime CreatedAtUtc { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Entities with an optimistic concurrency token (SQL Server rowversion).</summary>
public interface IHasRowVersion
{
    byte[] RowVersion { get; set; }
}
