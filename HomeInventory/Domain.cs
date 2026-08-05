using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

public sealed class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

public sealed class Floor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    public Property? Property { get; set; }
    public List<Room> Rooms { get; set; } = [];
}

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PropertyId { get; set; }
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

public sealed class Surface
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

public sealed class Paint
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

public sealed class PropertyPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Property? Property { get; set; }
}

public sealed class RoomPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Room? Room { get; set; }
}

public sealed class Fixture
{
    public Guid Id { get; set; } = Guid.NewGuid();
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
    public Room? Room { get; set; }
    public List<FixturePhoto> Photos { get; set; } = [];
}

public sealed class FixturePhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FixtureId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public Fixture? Fixture { get; set; }
}

public sealed class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public Guid? ParentId { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public Property? Property { get; set; }
    public StorageLocation? Parent { get; set; }
}

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

public sealed class AssetPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public required string StorageKey { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
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

    protected override void OnModelCreating(ModelBuilder model)
    {
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
        model.Entity<FixturePhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<FixturePhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<StorageLocation>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Asset>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        model.Entity<Asset>().Property(x => x.Category).HasMaxLength(100).IsRequired();
        model.Entity<AssetPhoto>().Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        model.Entity<AssetPhoto>().Property(x => x.Caption).HasMaxLength(240);
        model.Entity<Floor>().HasOne(x => x.Property).WithMany(x => x.Floors).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
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
        model.Entity<AssetPhoto>().HasOne(x => x.Asset).WithMany(x => x.Photos).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}
