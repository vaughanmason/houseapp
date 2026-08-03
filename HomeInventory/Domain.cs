using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

// TODO: Model floors, photos, documents, contacts, manufacturers, warranties, and paint as first-class entities.
public sealed class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Address { get; set; }
    public decimal? FloorArea { get; set; }
    public string? Notes { get; set; }
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
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string? Notes { get; set; }
    public Property? Property { get; set; }
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
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Asset> Assets => Set<Asset>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Property>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Room>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<StorageLocation>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<Asset>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        model.Entity<Asset>().Property(x => x.Category).HasMaxLength(100).IsRequired();
        model.Entity<Room>().HasOne(x => x.Property).WithMany(x => x.Rooms).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Property).WithMany(x => x.StorageLocations).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StorageLocation>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.Property).WithMany(x => x.Assets).HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Asset>().HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Asset>().HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
