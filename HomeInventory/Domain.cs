using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

/// <summary>Entities that can be exported and re-imported. ExternalId keeps the backup ID so restoring the same backup twice reuses existing rows.</summary>
public interface IHasExternalId
{
    Guid Id { get; }
    string? ExternalId { get; set; }
}

public sealed class Property : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? FloorArea { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }
    public List<PropertyPhoto> Photos { get; set; } = [];
    public List<Floor> Floors { get; set; } = [];
    public List<StorageLocation> StorageLocations { get; set; } = [];
    public List<Asset> Assets { get; set; } = [];
}

public sealed class Floor : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public Property? Property { get; set; }
    public List<Room> Rooms { get; set; } = [];
}

public sealed class Room : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? FloorId { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public decimal? Area { get; set; }
    public decimal? Volume { get; set; }
    public decimal? CeilingHeight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string? Flooring { get; set; }
    public string? WallFinish { get; set; }
    public string? CeilingFinish { get; set; }
    public string? PaintDetails { get; set; }
    public int? WindowsCount { get; set; }
    public int? DoorsCount { get; set; }
    public string? FixturesNotes { get; set; }
    public string? UtilitiesNotes { get; set; }
    public string? Notes { get; set; }
    public Floor? Floor { get; set; }
    public List<RoomPhoto> Photos { get; set; } = [];
    public List<RoomPaint> RoomPaints { get; set; } = [];
    public List<Surface> Surfaces { get; set; } = [];
    public List<Fixture> Fixtures { get; set; } = [];
}

public sealed class Surface : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid RoomId { get; set; }
    public required string Name { get; set; }
    public required string SurfaceType { get; set; }
    public string? PaintBrand { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Finish { get; set; }
    public int? Coats { get; set; }
    public DateOnly? PaintedDate { get; set; }
    public string? Painter { get; set; }
    public decimal? QuantityPurchased { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
    public string? Material { get; set; }
    public string? Supplier { get; set; }
    public string? Warranty { get; set; }
    public string? Invoice { get; set; }
    public DateOnly? InstallationDate { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public Room? Room { get; set; }
}

public sealed class Paint : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public required string Brand { get; set; }
    public required string ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Finish { get; set; }
    public string? Notes { get; set; }
    public List<RoomPaint> RoomPaints { get; set; } = [];
}

public sealed class RoomPaint
{
    public Guid RoomId { get; set; }
    public Guid PaintId { get; set; }
    public int SortOrder { get; set; }
    public string? Surface { get; set; }
    public Room? Room { get; set; }
    public Paint? Paint { get; set; }
}

public sealed class PropertyPhoto : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Property? Property { get; set; }
}

public sealed class RoomPhoto : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid RoomId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Room? Room { get; set; }
}

public sealed class Fixture : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid RoomId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? CurrentValue { get; set; }
    public string? Warranty { get; set; }
    public string? ManualUrl { get; set; }
    public string? InstallerName { get; set; }
    public DateOnly? InstallationDate { get; set; }
    public string? MaintenanceSchedule { get; set; }
    public DateOnly? LastMaintenanceDate { get; set; }
    public string? Condition { get; set; }
    public string? Notes { get; set; }
    /// <summary>"Fixture" (sink, cupboard...) or "Utility" (meter, inverter, internet...); utilities reuse everything fixtures have.</summary>
    public string Category { get; set; } = "Fixture";
    /// <summary>Utility provider and account/meter number, e.g. the electricity supplier.</summary>
    public string? Provider { get; set; }
    public string? AccountNumber { get; set; }
    public Room? Room { get; set; }
    public List<FixturePhoto> Photos { get; set; } = [];
}

public sealed class FixturePhoto : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid FixtureId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Fixture? Fixture { get; set; }
}

public sealed class StorageLocation : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public Property? Property { get; set; }
    public StorageLocation? Parent { get; set; }
}

public sealed class Asset : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? StorageLocationId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? CurrentValue { get; set; }
    public string? Condition { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public Property? Property { get; set; }
    public Room? Room { get; set; }
    public StorageLocation? StorageLocation { get; set; }
    public List<AssetPhoto> Photos { get; set; } = [];
}

public sealed class AssetPhoto : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid AssetId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Asset? Asset { get; set; }
}

public sealed class MaintenanceTask : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? FixtureId { get; set; }
    public required string Title { get; set; }
    /// <summary>Repeat every <see cref="IntervalValue"/> <see cref="IntervalUnit"/> (days, months or years); both null for a one-off task.</summary>
    public int? IntervalValue { get; set; }
    public string? IntervalUnit { get; set; }
    public DateOnly? DueOn { get; set; }
    public DateOnly? LastCompletedOn { get; set; }
    public string? Supplier { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? Notes { get; set; }
    public Property? Property { get; set; }
    public Fixture? Fixture { get; set; }
    public List<MaintenanceRecord> Records { get; set; } = [];
}

public sealed class MaintenanceRecord : IHasExternalId
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid TaskId { get; set; }
    public DateOnly CompletedOn { get; set; }
    public decimal? Cost { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public MaintenanceTask? Task { get; set; }
}

/// <summary>A file (receipt, manual, warranty...) stored by <see cref="FileStore"/>, owned by a property and optionally attached to one room, fixture, asset or maintenance task.</summary>
public sealed class Document : IHasExternalId
{
    public static readonly string[] Kinds = ["Receipt", "Invoice", "Manual", "Warranty", "Insurance", "Certificate", "Plan", "Photo", "Other"];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? FixtureId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? MaintenanceTaskId { get; set; }
    public required string Title { get; set; }
    public required string Kind { get; set; }
    public required string StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Tags { get; set; }
    public string? Notes { get; set; }
    public Property? Property { get; set; }
    public Room? Room { get; set; }
    public Fixture? Fixture { get; set; }
    public Asset? Asset { get; set; }
    public MaintenanceTask? MaintenanceTask { get; set; }
}

/// <summary>A key event in an asset's life: recorded automatically (added, moved, archived) or entered by hand (repaired, serviced, valued, note).</summary>
public sealed class AssetEvent : IHasExternalId
{
    public static readonly string[] AutomaticKinds = ["Added", "Moved", "Archived", "Unarchived"];
    public static readonly string[] ManualKinds = ["Repaired", "Serviced", "Valued", "Note"];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public Guid AssetId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public required string Kind { get; set; }
    public string? Description { get; set; }
    public decimal? Cost { get; set; }
    public Asset? Asset { get; set; }
}

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Surface> Surfaces => Set<Surface>();
    public DbSet<Paint> Paints => Set<Paint>();
    public DbSet<RoomPaint> RoomPaints => Set<RoomPaint>();
    public DbSet<PropertyPhoto> PropertyPhotos => Set<PropertyPhoto>();
    public DbSet<RoomPhoto> RoomPhotos => Set<RoomPhoto>();
    public DbSet<Fixture> Fixtures => Set<Fixture>();
    public DbSet<FixturePhoto> FixturePhotos => Set<FixturePhoto>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetPhoto> AssetPhotos => Set<AssetPhoto>();
    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var model = modelBuilder;
        model.Entity<Property>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Property>().Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("USD").IsRequired();
        model.Entity<Floor>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Room>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Surface>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Surface>().Property(x => x.SurfaceType).HasMaxLength(40).IsRequired();
        model.Entity<Surface>().Property(x => x.PaintBrand).HasMaxLength(120);
        model.Entity<Surface>().Property(x => x.ColorName).HasMaxLength(120);
        model.Entity<Surface>().Property(x => x.ColorCode).HasMaxLength(80);
        model.Entity<Surface>().Property(x => x.Finish).HasMaxLength(80);
        model.Entity<Surface>().Property(x => x.Painter).HasMaxLength(120);
        model.Entity<Surface>().Property(x => x.Manufacturer).HasMaxLength(120);
        model.Entity<Surface>().Property(x => x.ProductName).HasMaxLength(160);
        model.Entity<Surface>().Property(x => x.Material).HasMaxLength(120);
        model.Entity<Surface>().Property(x => x.Supplier).HasMaxLength(160);
        model.Entity<Surface>().Property(x => x.Warranty).HasMaxLength(160);
        model.Entity<Surface>().Property(x => x.Invoice).HasMaxLength(240);
        model.Entity<Surface>().Property(x => x.Notes).HasMaxLength(800);
        model.Entity<Paint>().Property(x => x.Brand).HasMaxLength(120).IsRequired();
        model.Entity<Paint>().Property(x => x.ColorName).HasMaxLength(120).IsRequired();
        model.Entity<Paint>().Property(x => x.ColorCode).HasMaxLength(80);
        model.Entity<Paint>().Property(x => x.Finish).HasMaxLength(80);
        model.Entity<Paint>().Property(x => x.Notes).HasMaxLength(800);
        model.Entity<RoomPaint>().Property(x => x.Surface).HasMaxLength(120);
        model.Entity<RoomPaint>().HasKey(x => new { x.RoomId, x.PaintId });
        model.Entity<PropertyPhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<PropertyPhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<RoomPhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<RoomPhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<Fixture>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        model.Entity<Fixture>().Property(x => x.Type).HasMaxLength(80).IsRequired();
        model.Entity<Fixture>().Property(x => x.Manufacturer).HasMaxLength(160);
        model.Entity<Fixture>().Property(x => x.Model).HasMaxLength(160);
        model.Entity<Fixture>().Property(x => x.SerialNumber).HasMaxLength(160);
        model.Entity<Fixture>().Property(x => x.Warranty).HasMaxLength(240);
        model.Entity<Fixture>().Property(x => x.ManualUrl).HasMaxLength(512);
        model.Entity<Fixture>().Property(x => x.InstallerName).HasMaxLength(160);
        model.Entity<Fixture>().Property(x => x.MaintenanceSchedule).HasMaxLength(400);
        model.Entity<Fixture>().Property(x => x.Condition).HasMaxLength(80);
        model.Entity<Fixture>().Property(x => x.Notes).HasMaxLength(1000);
        model.Entity<Fixture>().Property(x => x.Category).HasMaxLength(20).HasDefaultValue("Fixture").IsRequired();
        model.Entity<Fixture>().Property(x => x.Provider).HasMaxLength(160);
        model.Entity<Fixture>().Property(x => x.AccountNumber).HasMaxLength(120);
        model.Entity<FixturePhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<FixturePhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<StorageLocation>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Asset>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        model.Entity<Asset>().Property(x => x.Category).HasMaxLength(100).IsRequired();
        model.Entity<AssetPhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<AssetPhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<Floor>().HasOne(x => x.Property).WithMany(x => x.Floors).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        // Room.PropertyId is denormalized from its floor; the FK mirrors the original Rooms table.
        model.Entity<Room>().HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Room>().HasOne(x => x.Floor).WithMany(x => x.Rooms).HasForeignKey(x => x.FloorId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Surface>().HasOne(x => x.Room).WithMany(x => x.Surfaces).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PropertyPhoto>().HasOne(x => x.Property).WithMany(x => x.Photos).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPhoto>().HasOne(x => x.Room).WithMany(x => x.Photos).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Fixture>().HasOne(x => x.Room).WithMany(x => x.Fixtures).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<FixturePhoto>().HasOne(x => x.Fixture).WithMany(x => x.Photos).HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPaint>().HasOne(x => x.Room).WithMany(x => x.RoomPaints).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPaint>().HasOne(x => x.Paint).WithMany(x => x.RoomPaints).HasForeignKey(x => x.PaintId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Property).WithMany(x => x.StorageLocations).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.Property).WithMany(x => x.Assets).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Asset>().HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MaintenanceTask>().Property(x => x.Title).HasMaxLength(200).IsRequired();
        model.Entity<MaintenanceTask>().Property(x => x.IntervalUnit).HasMaxLength(10);
        model.Entity<MaintenanceTask>().Property(x => x.Supplier).HasMaxLength(160);
        model.Entity<MaintenanceTask>().Property(x => x.Notes).HasMaxLength(1000);
        model.Entity<MaintenanceTask>().HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MaintenanceTask>().HasOne(x => x.Fixture).WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<MaintenanceTask>().HasIndex(x => x.DueOn);
        model.Entity<MaintenanceRecord>().Property(x => x.Supplier).HasMaxLength(160);
        model.Entity<MaintenanceRecord>().Property(x => x.Notes).HasMaxLength(1000);
        model.Entity<MaintenanceRecord>().HasOne(x => x.Task).WithMany(x => x.Records).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Document>().Property(x => x.Title).HasMaxLength(200).IsRequired();
        model.Entity<Document>().Property(x => x.Kind).HasMaxLength(40).IsRequired();
        model.Entity<Document>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<Document>().Property(x => x.FileName).HasMaxLength(260);
        model.Entity<Document>().Property(x => x.ContentType).HasMaxLength(100);
        model.Entity<Document>().Property(x => x.Tags).HasMaxLength(400);
        model.Entity<Document>().Property(x => x.Notes).HasMaxLength(2000);
        model.Entity<Document>().HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        // Deleting what a document is attached to keeps the document at property level rather than losing a receipt or warranty.
        model.Entity<Document>().HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Document>().HasOne(x => x.Fixture).WithMany().HasForeignKey(x => x.FixtureId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Document>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Document>().HasOne(x => x.MaintenanceTask).WithMany().HasForeignKey(x => x.MaintenanceTaskId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Document>().HasIndex(x => x.ExpiresOn);
        model.Entity<AssetEvent>().Property(x => x.Kind).HasMaxLength(20).IsRequired();
        model.Entity<AssetEvent>().Property(x => x.Description).HasMaxLength(1000);
        model.Entity<AssetEvent>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        foreach (var type in model.Model.GetEntityTypes().Select(x => x.ClrType).Where(typeof(IHasExternalId).IsAssignableFrom).ToList())
        {
            model.Entity(type).Property<string?>(nameof(IHasExternalId.ExternalId)).HasMaxLength(200);
            model.Entity(type).HasIndex(nameof(IHasExternalId.ExternalId));
        }
        model.Entity<AssetPhoto>().HasOne(x => x.Asset).WithMany(x => x.Photos).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
