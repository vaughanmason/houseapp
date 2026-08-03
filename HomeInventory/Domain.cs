using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

// TODO: Model floors, photos, documents, contacts, manufacturers, warranties, and paint as first-class entities.
public sealed class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Address { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? FloorArea { get; set; }
    public string? Notes { get; set; }
    public List<PropertyPhoto> Photos { get; set; } = [];
    public List<Room> Rooms { get; set; } = [];
    public List<StorageLocation> StorageLocations { get; set; } = [];
    public List<Asset> Assets { get; set; } = [];
}
public sealed class Room
{
    // TODO: Add room area/volume and permanent details for surfaces, windows, doors, fixtures, and utilities.
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
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
    public Property? Property { get; set; }
    public List<RoomPhoto> Photos { get; set; } = [];
    public List<RoomPaint> RoomPaints { get; set; } = [];
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
    // TODO: Add barcode/QR, attachment, warranty, and immutable lifecycle-history records for assets.
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
}
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    // TODO: Add DbSets and relationship rules for the planned maintenance, paint, document, photo, and utility modules.
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Paint> Paints => Set<Paint>();
    public DbSet<RoomPaint> RoomPaints => Set<RoomPaint>();
    public DbSet<PropertyPhoto> PropertyPhotos => Set<PropertyPhoto>();
    public DbSet<RoomPhoto> RoomPhotos => Set<RoomPhoto>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Asset> Assets => Set<Asset>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Property>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Room>().Property(x => x.Name).HasMaxLength(160).IsRequired();
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
        model.Entity<StorageLocation>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Asset>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        model.Entity<Asset>().Property(x => x.Category).HasMaxLength(100).IsRequired();
        model.Entity<Room>().HasOne(x => x.Property).WithMany(x => x.Rooms).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PropertyPhoto>().HasOne(x => x.Property).WithMany(x => x.Photos).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPhoto>().HasOne(x => x.Room).WithMany(x => x.Photos).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPaint>().HasOne(x => x.Room).WithMany(x => x.RoomPaints).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RoomPaint>().HasOne(x => x.Paint).WithMany(x => x.RoomPaints).HasForeignKey(x => x.PaintId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Property).WithMany(x => x.StorageLocations).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.Property).WithMany(x => x.Assets).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Asset>().HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
