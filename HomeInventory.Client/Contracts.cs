namespace HomeInventory.Client;

// TODO: Extend these API contracts in lockstep with the planned floor, surface, fixture, maintenance, document, photo, utility, and paint modules.
public sealed record PropertyDto(Guid Id, string Name, string? Address, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? FloorArea, string? Notes);
public sealed record RoomDto(Guid Id, Guid PropertyId, string Name, string? Type, decimal? Area, decimal? Volume, decimal? CeilingHeight, decimal? Length, decimal? Width, decimal? Height, string? Flooring, string? WallFinish, string? CeilingFinish, string? PaintDetails, int? WindowsCount, int? DoorsCount, string? FixturesNotes, string? UtilitiesNotes, string? Notes);
public sealed record PhotoMetadataDto(Guid Id, string StorageKey, string? Caption, int SortOrder);
public sealed record PaintDto(Guid Id, string Brand, string ColorName, string? ColorCode, string? Finish, string? Notes);
public sealed record RoomPaintDto(Guid PaintId, string Brand, string ColorName, string? ColorCode, string? Finish, string? Notes, int SortOrder, string? Surface);
public sealed record StorageLocationDto(Guid Id, Guid PropertyId, Guid? ParentId, string Name, string? Type, string Path);
public sealed record AssetDto(Guid Id, Guid PropertyId, Guid? RoomId, Guid? StorageLocationId, string Name, string Category, string? Description, string? Brand, string? Model, string? SerialNumber, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? CurrentValue, string? Condition, string? Notes, bool IsArchived, string? LocationPath);
public sealed record DashboardDto(int AssetCount, decimal TotalValue, IReadOnlyList<CategoryTotalDto> Categories);
public sealed record CategoryTotalDto(string Category, decimal Total);
public sealed record SearchResultDto(string Kind, Guid Id, string Title, string Detail, string? LocationPath);

public sealed record PropertyInput(string Name, string? Address, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? FloorArea, string? Notes);
public sealed record RoomInput(Guid PropertyId, string Name, string? Type, decimal? Area, decimal? Volume, decimal? CeilingHeight, decimal? Length, decimal? Width, decimal? Height, string? Flooring, string? WallFinish, string? CeilingFinish, string? PaintDetails, int? WindowsCount, int? DoorsCount, string? FixturesNotes, string? UtilitiesNotes, string? Notes);
public sealed record PhotoMetadataInput(string StorageKey, string? Caption, int SortOrder);
public sealed record PaintInput(string Brand, string ColorName, string? ColorCode, string? Finish, string? Notes);
public sealed record RoomPaintInput(Guid PaintId, int SortOrder, string? Surface);
public sealed record StorageLocationInput(Guid PropertyId, Guid? ParentId, string Name, string? Type);
public sealed record AssetInput(Guid PropertyId, Guid? RoomId, Guid? StorageLocationId, string Name, string Category, string? Description, string? Brand, string? Model, string? SerialNumber, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? CurrentValue, string? Condition, string? Notes);
public sealed record AssetMoveInput(Guid? RoomId, Guid? StorageLocationId);

public sealed record InventoryExport(int SchemaVersion, List<ImportProperty> Properties, List<ImportRoom> Rooms, List<ImportStorageLocation> StorageLocations, List<ImportAsset> Assets);
public sealed record ImportProperty(string ExternalId, string Name, string? Address, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? FloorArea, string? Notes);
public sealed record ImportRoom(string ExternalId, string PropertyExternalId, string Name, string? Type, decimal? Area, decimal? Volume, decimal? CeilingHeight, decimal? Length, decimal? Width, decimal? Height, string? Flooring, string? WallFinish, string? CeilingFinish, string? PaintDetails, int? WindowsCount, int? DoorsCount, string? FixturesNotes, string? UtilitiesNotes, string? Notes);
public sealed record ImportStorageLocation(string ExternalId, string PropertyExternalId, string? ParentExternalId, string Name, string? Type);
public sealed record ImportAsset(string ExternalId, string PropertyExternalId, string? RoomExternalId, string? StorageLocationExternalId, string Name, string Category, string? Description, string? Brand, string? Model, string? SerialNumber, DateOnly? PurchaseDate, decimal? PurchasePrice, decimal? CurrentValue, string? Condition, string? Notes);
public sealed record ImportPreviewDto(bool IsValid, IReadOnlyList<string> Errors, int Properties, int Rooms, int StorageLocations, int Assets, IReadOnlyList<string> DuplicateExternalIds);
