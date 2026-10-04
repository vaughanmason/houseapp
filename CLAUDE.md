# CLAUDE.md

Guidance for Claude Code when working in this repository. See also `README.md` (feature/API overview) and `documents/` (outstanding work, run check, product plan).

## Commands

```powershell
dotnet restore .\HomeInventory.sln
dotnet build .\HomeInventory.sln
dotnet run --project .\HomeInventory --launch-profile http     # http://localhost:5068
dotnet run --project .\HomeInventory --launch-profile https    # https://localhost:7060 + http://localhost:5068
dotnet test .\HomeInventory.Tests
dotnet test .\HomeInventory.Tests --filter "FullyQualifiedName~CreateAsset_AndArchive"   # single test
```

- Target framework is `net10.0` (SDK 10.x). There is no linter or CI configured.
- The build currently emits one nullable warning (`InventoryApi.cs` `FixtureDtos`); don't add new warnings.
- Running the app creates or migrates the real local database at `%LOCALAPPDATA%\HomeInventory\inventory.db`. Tests never touch it.

## Architecture

- `HomeInventory` is the ASP.NET Core host. `Program.cs` registers SQLite, runs `Database.MigrateAsync()` on startup, serves the Blazor WebAssembly client through `Components/App.razor`, and calls `app.MapInventoryApi()`. `public partial class Program;` exists so `WebApplicationFactory<Program>` can be used in tests.
- `HomeInventory\Domain.cs` holds every EF entity plus `InventoryDbContext` (constraints and relationships live in `OnModelCreating`).
- `HomeInventory\InventoryApi.cs` is the **entire** `/api` surface: one consolidated minimal-API module with static `ToDto`/`*Dtos` helpers at the bottom. Keep that style; don't introduce controllers, services or MediatR.
- `HomeInventory.Client` is the interactive WASM UI. Pages in `Pages/` call relative `api/...` URLs with an injected `HttpClient`. Shared components live in `Components/` (`CurrencyDisplay`, `Breadcrumb`, `ConfirmDialog`, `PropertySelector`). `Counter.razor`/`Weather.razor` are unused template leftovers.
- `HomeInventory.Client\Contracts.cs` holds **all** wire records (DTOs, `*Input`, `Import*`, `InventoryExport`). The server consumes them through its project reference. Keep these, `InventoryApi.cs`, and the client pages in sync.
- `HomeInventory.Tests` uses xUnit with `CustomWebApplicationFactory`, which replaces the DbContext with a shared in-memory SQLite connection (migrations still run against it). Tests are HTTP-level integration tests in `InventoryApiTests.cs`.

## Domain model

```
Property (Currency, Photos)
├── Floor
│   └── Room            (Room.PropertyId is a denormalized copy of Floor.PropertyId; set it whenever FloorId changes)
│       ├── Surface     (SurfaceType: wall/ceiling/flooring/trim + paint/material metadata)
│       ├── Fixture ── FixturePhoto
│       ├── RoomPhoto
│       └── RoomPaint ──► Paint   (global paint library; composite key RoomId+PaintId)
├── StorageLocation     (self-referencing tree via ParentId, scoped to one property)
├── Asset ── AssetPhoto (optional RoomId XOR StorageLocationId)
└── PropertyPhoto
```

Photos are metadata only (`StorageKey`, `Caption`, `SortOrder`). There is no file upload or storage.

## Data and API conventions

- A property is the ownership boundary. Rooms (through their floor), storage locations and assets must belong to the same property. Validate this in any new or changed endpoint (see `ValidateAsset` and the `/assets/{id}/move` handler for the pattern).
- An asset may have a room **or** a storage location, never both.
- `StorageLocationDto.Path` and `AssetDto.LocationPath` are computed server-side in `Parent → Child` format. Don't rebuild paths in the client.
- Assets are archived with `IsArchived`. Dashboard, search, default listings and export only use active assets. Prefer archiving to deletion for assets.
- Delete behavior (from `OnModelCreating`): Property → Floors/StorageLocations/Assets/PropertyPhotos cascade. Floor → Rooms cascade (**EF model only**: `Rooms.FloorId` has no DB foreign key, so `DELETE /floors/{id}` must `Include(x => x.Rooms)` for EF to delete them). Room → Surfaces/Fixtures/RoomPhotos/RoomPaints cascade. Fixture → photos and Asset → photos cascade. Paint → RoomPaints cascade. **Restrict:** StorageLocation.Parent, Asset.Room, Asset.StorageLocation. Delete endpoints check for dependents and return `BadRequest` first (see the floor, room and location delete handlers). Follow that pattern.
- Handlers trim optional strings (`?.Trim()`), return `Results.ValidationProblem` for missing required fields, `Results.BadRequest("message")` for invalid cross-entity references, `NotFound` for missing route entities, and `Created($"/api/...", dto)` on POST. Status-code re-execution to `/not-found` is applied only to non-`/api` paths (`Program.cs`), so API clients get raw status codes.
- Currency is a per-property 3-letter ISO code normalized by `NormalizeCurrency` (blank → `"USD"`, invalid → `null` → validation error).

## Migrations

- Migrations under `HomeInventory\Migrations` are **hand-written**. Each class carries `[DbContext(typeof(InventoryDbContext))]` and `[Migration("yyyyMMddHHmmss_Name")]`, and there are **no `.Designer.cs` files and no model snapshot**. `dotnet-ef` is not installed.
- To change the model, add a new hand-written migration in the same style with a timestamp later than the last one, then update `Domain.cs`. Don't run `dotnet ef migrations add` as-is: without a snapshot it scaffolds the whole schema again.
- Every relationship or column change needs a migration. The app migrates the user's real database on startup.

## Import/export contract

- Backups use `InventoryExport` with `schemaVersion: 1` and string external IDs (the export uses database GUIDs as strings). Children refer to parents through `*ExternalId` fields.
- Exported: properties, floors, rooms, surfaces, storage locations, active assets (and their photos), property photos, fixtures, fixture photos, room photos, paints, room-paint assignments (external ID `"{roomId}:{paintId}"`, since the table has a composite key). On import, paints that match an existing paint (brand + colour name + code, case-insensitive) are reused, because the library is global.
- The client always calls `POST /api/import/preview` before `POST /api/import/confirm` (`{ inventory, skipExternalIds }`). Confirm re-runs preview, then inserts everything in one transaction. Duplicate assets are flagged in preview by matching **name + location path** against existing active assets, and the client passes those IDs back as `skipExternalIds`.
- Every collection after `PropertyPhotos` (`Fixtures`, `AssetPhotos`, `Paints`, `RoomPaints`, `RoomPhotos`, `FixturePhotos`) is optional (nullable) on `InventoryExport` for backward compatibility. Keep new collections optional the same way. When you change the schema, update `Export`, `Preview` and the confirm handler together.

## When changing things

- For a new or changed endpoint, update `Contracts.cs`, `InventoryApi.cs`, the client page, the README API table, and add or extend a test in `InventoryApiTests.cs`.
- Known bugs and planned modules are tracked in `documents/OUTSTANDING_WORK.md`. Check it before starting work and update it when you fix something.
