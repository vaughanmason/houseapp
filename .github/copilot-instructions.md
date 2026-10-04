# Home Inventory Copilot Instructions

## Commands

```powershell
dotnet restore .\HomeInventory.sln
dotnet build .\HomeInventory.sln
dotnet run --project .\HomeInventory
dotnet test .\HomeInventory.Tests
dotnet test .\HomeInventory.Tests --filter "FullyQualifiedName~CreateAsset_AndArchive"
```

The server launch profile uses `https://localhost:7060` and `http://localhost:5068`. Tests are xUnit HTTP integration tests in `HomeInventory.Tests` using `CustomWebApplicationFactory` (shared in-memory SQLite). There is no linter or CI configured.

## Architecture

- `HomeInventory` is the ASP.NET Core host. It serves the Blazor WebAssembly client, exposes the `/api` minimal API group, and applies EF Core migrations during startup.
- `HomeInventory.Client` is the interactive WebAssembly UI, with one page per entity (Properties, Floors, Rooms, Fixtures, Paints, Assets & Storage, Import & backup). Pages call relative `/api` endpoints with `HttpClient`. The host registers the client assembly in `Program.cs` and renders it through `Components/App.razor`.
- Server persistence is SQLite through `InventoryDbContext` in `HomeInventory\Domain.cs`. The database is deliberately local-only at `%LOCALAPPDATA%\HomeInventory\inventory.db`, not in the repository.
- API wire records live in `HomeInventory.Client\Contracts.cs`, even though the server consumes them through its project reference. Keep those records and endpoint request/response shapes synchronized with client pages and `HomeInventory\InventoryApi.cs`.
- `InventoryApi.MapInventoryApi` contains the full application API: properties, floors, rooms, surfaces, fixtures, paints and room-paint assignments, photo metadata, storage locations, assets, dashboard/search, and JSON export/import. It is intentionally a consolidated minimal-API module rather than controllers or service classes.

## Data and API conventions

- Hierarchy: Property → Floor → Room → (Surface, Fixture, RoomPhoto, RoomPaint → Paint). Property → (StorageLocation tree, Asset, PropertyPhoto). `Room.PropertyId` is denormalized from its floor and must be updated whenever `FloorId` changes.
- A property is the ownership boundary. Rooms (through their floor), storage locations and assets must reference the same existing property. Validate that invariant in any new or updated endpoint. An asset has a room or a storage location, never both.
- Storage locations form a self-referencing tree scoped to one property. `StorageLocationDto.Path` is computed server-side with the `Parent → Child` display format. Preserve this behavior for API consumers rather than reconstructing paths in the client.
- Assets are archived with `IsArchived`. Dashboard, search, default asset listings and export all operate on active assets only. Do not replace archiving with deletion.
- Cascades: Property → floors/locations/assets/property photos, Floor → rooms, Room → surfaces/fixtures/room photos/room paints, Fixture/Asset → their photos, Paint → room paints. Restrictive: storage-location parent, asset room, asset storage location. Check for dependent assets before deleting rooms or floors.
- Migrations under `HomeInventory\Migrations` are hand-written (no `.Designer.cs` files or model snapshot). Add a new migration in the same style for every model change. Don't scaffold with `dotnet ef migrations add` as-is.
- Endpoint handlers trim optional text fields, return `Results.ValidationProblem` for required-field validation where established, and use `Results.BadRequest` for invalid cross-entity references. Maintain these response patterns for existing resource types.
- Currency is a per-property 3-letter ISO code normalized by `NormalizeCurrency` (defaults to `USD`).

## Import/export contract

- Backups use `InventoryExport` with `schemaVersion: 1` and external IDs, not database IDs. Child records refer to parent records through the relevant `*ExternalId` fields. Newer collections (`fixtures`, `assetPhotos`) are optional for backward compatibility.
- Paints, room-paint assignments, room photos and fixture photos are not yet exported.
- Import always calls `/api/import/preview` before `/api/import/confirm`. Preview flags likely duplicate active assets (matched by name + location path), and confirm imports inside a transaction, skipping the external IDs the client passes back from the preview result. Keep preview validation and confirm behavior aligned when changing this schema.
