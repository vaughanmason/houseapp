# CLAUDE.md

Guidance for Claude Code when working in this repository. See also `README.md` (feature/API overview) and `documents/` (outstanding work, run check, product plan).

## Commands

```powershell
dotnet restore .\HomeInventory.sln
dotnet tool restore                                             # installs the pinned dotnet-ef local tool
dotnet build .\HomeInventory.sln
dotnet run --project .\HomeInventory --launch-profile http     # http://localhost:5068
dotnet run --project .\HomeInventory --launch-profile https    # https://localhost:7060 + http://localhost:5068
dotnet test .\HomeInventory.Tests
dotnet test .\HomeInventory.Tests --filter "FullyQualifiedName~CreateAsset_AndArchive"   # single test
dotnet ef migrations add <Name> --project HomeInventory        # after changing Domain.cs
dotnet ef migrations script <From> <To> --project HomeInventory  # review the SQL before running the app
```

- Target framework is `net10.0` (SDK 10.x). There is no linter or CI configured.
- The build is warning-free. Keep it that way.
- Running the app creates or migrates the real local database at `%LOCALAPPDATA%\HomeInventory\inventory.db`. Tests never touch it.

## Architecture

- `HomeInventory` is the ASP.NET Core host. `Program.cs` registers SQLite, runs `Database.MigrateAsync()` on startup, serves the Blazor WebAssembly client through `Components/App.razor`, and calls `app.MapInventoryApi()`. `public partial class Program;` exists so `WebApplicationFactory<Program>` can be used in tests.
- `HomeInventory\Domain.cs` holds every EF entity plus `InventoryDbContext` (constraints and relationships live in `OnModelCreating`).
- `HomeInventory\InventoryApi.cs` is the **entire** `/api` surface: one consolidated minimal-API module with static `ToDto`/`*Dtos` helpers at the bottom. Keep that style; don't introduce controllers, services or MediatR.
- `HomeInventory.Client` is the interactive WASM UI. Pages in `Pages/` call relative `api/...` URLs with an injected `HttpClient`. Shared components live in `Components/` (`CurrencyDisplay`, `Breadcrumb`, `ConfirmDialog`, `PropertySelector`). `Counter.razor`/`Weather.razor` are unused template leftovers.
- `HomeInventory.Client\Contracts.cs` holds **all** wire records (DTOs, `*Input`, `Import*`, `InventoryExport`). The server consumes them through its project reference. Keep these, `InventoryApi.cs`, and the client pages in sync.
- `HomeInventory.Tests` uses xUnit with `CustomWebApplicationFactory`, which replaces the DbContext with a shared in-memory SQLite connection (migrations still run against it). Tests are HTTP-level integration tests in `InventoryApiTests.cs` and `InventoryValidationTests.cs` (which has `Create*Async` helpers for building fixtures).

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
- Assets are never hard-deleted. There is no delete endpoint: use `POST /assets/{id}/archive` and `/unarchive`. Dashboard, search, default listings and export only use active assets.
- Delete behavior (from `OnModelCreating`): Property → Floors/StorageLocations/Assets/PropertyPhotos cascade. Floor → Rooms cascade. Room also has a cascading FK to Property (`Room.PropertyId`). Room → Surfaces/Fixtures/RoomPhotos/RoomPaints cascade. Fixture → photos and Asset → photos cascade. Paint → RoomPaints cascade. **Restrict:** StorageLocation.Parent, Asset.Room, Asset.StorageLocation. Delete endpoints check for dependents and return `BadRequest` first (see the floor, room and location delete handlers). Follow that pattern.
- Handlers trim optional strings (`?.Trim()`), return `Results.ValidationProblem` for missing required fields, `Results.BadRequest("message")` for invalid cross-entity references, `NotFound` for missing route entities, and `Created($"/api/...", dto)` on POST. Status-code re-execution to `/not-found` is applied only to non-`/api` paths (`Program.cs`), so API clients get raw status codes.
- Currency is a per-property 3-letter ISO code normalized by `NormalizeCurrency` (blank → `"USD"`, invalid → `null` → validation error). Never sum money across currencies: the dashboard returns one `CurrencyTotalDto` per currency.

## Migrations

- `dotnet-ef` 10.0.10 is pinned in `dotnet-tools.json` (`dotnet tool restore`). `InventoryDbContextModelSnapshot.cs` exists as of `20261004160308_AlignRoomsAndAddAssetExternalId`, so scaffold new migrations normally with `dotnet ef migrations add`.
- The six migrations before that were hand-written, without designer files. Leave them alone.
- SQLite can't add foreign keys or alter columns in place. EF turns those operations into a table rebuild (`ef_temp_*`), which needs the migration's `.Designer.cs`. Always keep the generated designer file, and review the SQL with `dotnet ef migrations script`.
- Every relationship or column change needs a migration. The app migrates the user's real database on startup, so test a migration against a **copy** of `inventory.db` (`dotnet ef database update --connection "Data Source=<copy>"`) before running the app.

## Import/export contract

- Backups use `InventoryExport` with `schemaVersion: 1` and string external IDs (database GUIDs as strings, except for assets: see below). Children refer to parents through `*ExternalId` fields.
- Exported: properties, floors, rooms, surfaces, storage locations, active assets (and their photos), property photos, fixtures, fixture photos, room photos, paints, room-paint assignments (external ID `"{roomId}:{paintId}"`, since the table has a composite key). On import, paints that match an existing paint (brand + colour name + code, case-insensitive) are reused, because the library is global.
- The client always calls `POST /api/import/preview` before `POST /api/import/confirm` (`{ inventory, skipExternalIds }`). Confirm re-runs preview, then inserts everything in one transaction. Duplicate assets are flagged in preview by **external ID**: an import asset is a duplicate when an active asset has the same `Asset.ExternalId` (set on import) or the same database ID. The client passes those IDs back as `skipExternalIds`. Export writes `ExternalId ?? Id`, so IDs stay stable across backup → restore → backup cycles.
- Every collection after `PropertyPhotos` (`Fixtures`, `AssetPhotos`, `Paints`, `RoomPaints`, `RoomPhotos`, `FixturePhotos`) is optional (nullable) on `InventoryExport` for backward compatibility. Keep new collections optional the same way. When you change the schema, update `Export`, `Preview` and the confirm handler together.

## When changing things

- For a new or changed endpoint, update `Contracts.cs`, `InventoryApi.cs`, the client page, the README API table, and add or extend a test.
- Known bugs and planned modules are tracked in `documents/OUTSTANDING_WORK.md`. Check it before starting work and update it when you fix something.
