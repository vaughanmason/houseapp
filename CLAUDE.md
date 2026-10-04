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

- Target framework is `net10.0` (SDK 10.x). CI (`.github/workflows/ci.yml`) runs restore, a Release build with `-warnaserror`, and the tests on every push or PR to `master`.
- The build is warning-free with .NET analyzers at `latest-recommended` (`Directory.Build.props`), and CI uses `-warnaserror`. `.editorconfig` holds the style settings and the two deliberate rule suppressions (CA1716 for entity names, CA1707 for xUnit test names). Use culture-invariant string APIs (`StringComparison.Ordinal*`, `ToLowerInvariant`).
- `UseAppHost` is false, so the host runs through `dotnet HomeInventory.dll`. Windows Application Control on the dev machine blocks the freshly built `HomeInventory.exe`.
- Running the app creates or migrates the real local database at `%LOCALAPPDATA%\HomeInventory\inventory.db`. Tests never touch it.

## Architecture

- Stack decision (2026-10-04): stay on Blazor WebAssembly with the ASP.NET Core host and SQLite. Don't introduce Flutter, MAUI or a separate client. Multi-device access is planned through network access to this app.

- `HomeInventory` is the ASP.NET Core host. `Program.cs` registers SQLite, runs `Database.MigrateAsync()` on startup, serves the Blazor WebAssembly client through `Components/App.razor`, and calls `app.MapInventoryApi()`. `public partial class Program;` exists so `WebApplicationFactory<Program>` can be used in tests.
- `HomeInventory\Domain.cs` holds every EF entity plus `InventoryDbContext` (constraints and relationships live in `OnModelCreating`).
- `HomeInventory\InventoryApi.cs` is the **entire** `/api` surface: one consolidated minimal-API module with static `ToDto`/`*Dtos` helpers at the bottom. Keep that style; don't introduce controllers, services or MediatR.
- Search (`/api/search`) filters in SQL with `EF.Functions.Like` (escaping `%`, `_` and `\`). Only storage locations are matched in memory, because their paths are computed.
- External package: `QRCoder` (MIT) generates label QR codes as SVG (`GET /api/qr`). Everything else uses the ASP.NET Core and EF Core packages.
- `OrphanFileSweeper` (hosted service) removes unreferenced uploads older than 24 hours.
- `HomeInventory.Client` is the interactive WASM UI. Pages in `Pages/` call relative `api/...` URLs with an injected `HttpClient`. Shared components live in `Components/` (`CurrencyDisplay`, `Breadcrumb`, `ConfirmDialog` with optional `ChildContent`/`ConfirmDisabled`, `PropertySelector`). Use `CurrencyDisplay` for money; don't add new formatting helpers.
- `HomeInventory.Client\Contracts.cs` holds **all** wire records (DTOs, `*Input`, `Import*`, `InventoryExport`). The server consumes them through its project reference. Keep these, `InventoryApi.cs`, and the client pages in sync.
- `HomeInventory.Tests` uses xUnit with `CustomWebApplicationFactory`, which replaces the DbContext with a shared in-memory SQLite connection (migrations still run against it). Tests are HTTP-level integration tests in `InventoryApiTests.cs` and `InventoryValidationTests.cs` (which has `Create*Async` helpers for building fixtures).

## Domain model

```
Property (Currency, Photos)
├── Floor
│   └── Room            (FloorId required; Room.PropertyId is a denormalized copy of Floor.PropertyId, so set it whenever FloorId changes)
│       ├── Surface     (SurfaceType: wall/ceiling/flooring/trim + paint/material metadata)
│       ├── Fixture ── FixturePhoto   (Category "Fixture" or "Utility"; utilities add Provider/AccountNumber)
│       ├── RoomPhoto
│       └── RoomPaint ──► Paint   (global paint library; composite key RoomId+PaintId)
├── StorageLocation     (self-referencing tree via ParentId, scoped to one property)
├── Asset ── AssetPhoto, AssetEvent   (optional RoomId XOR StorageLocationId; AssetEvent = history)
├── MaintenanceTask ── MaintenanceRecord   (task optionally linked to one of the property's fixtures)
├── Document            (file + metadata; optionally attached to ONE room, fixture, asset or maintenance task)
Contact                 (global supplier/installer directory, not owned by a property)
└── PropertyPhoto
```

Photos and documents store a `StorageKey`. Keys matching `yyyy/MM/{guid}.ext` are files uploaded through `FileStore` (`HomeInventory\FileStore.cs`, rooted at `%LOCALAPPDATA%\HomeInventory\files` or `Storage:FilesPath`); anything else is an external path/URL. `FileStore` sniffs the type from the leading bytes (JPEG, PNG, GIF, WebP, HEIC, PDF), caps files at 20 MB, and only serves keys matching that pattern. After deleting anything that removes photos or documents (including cascades from property/floor/room/fixture deletes), call `DeleteUnusedFiles` with the keys gathered beforehand (`FileKeysFor`) so orphaned files are removed only when nothing else references them. Printable pages mark controls with `no-print` and print-only headings with `print-only` (rules in `HomeInventory/wwwroot/app.css`). On the client, use the shared `PhotoManager` component for photo lists and `PhotoManager.FileUrl(key)` for links.

Every importable entity implements `IHasExternalId` (`Domain.cs`). `OnModelCreating` gives every implementer an indexed `ExternalId` (max 200) in one loop, so a new importable entity only needs the interface (plus a migration).

## Data and API conventions

- A property is the ownership boundary. Rooms (through their floor), storage locations and assets must belong to the same property. Validate this in any new or changed endpoint (see `ValidateAsset` and the `/assets/{id}/move` handler for the pattern).
- An asset may have a room **or** a storage location, never both.
- `StorageLocationDto.Path` and `AssetDto.LocationPath` are computed server-side in `Parent → Child` format. Don't rebuild paths in the client.
- Assets are never hard-deleted. There is no delete endpoint: use `POST /assets/{id}/archive` and `/unarchive`. Dashboard, search, default listings and export only use active assets.
- Delete behavior (from `OnModelCreating`): Property → Floors/StorageLocations/Assets/PropertyPhotos cascade. Floor → Rooms cascade. Room also has a cascading FK to Property (`Room.PropertyId`). Room → Surfaces/Fixtures/RoomPhotos/RoomPaints cascade. Fixture → photos and Asset → photos cascade. Paint → RoomPaints cascade. **Restrict:** StorageLocation.Parent, Asset.Room, Asset.StorageLocation. Delete endpoints check for dependents and return `BadRequest` first (see the property, floor, room and location delete handlers). Follow that pattern. Because `StorageLocation.Parent` is `Restrict`, deleting a whole location tree must go leaf-first (see `DELETE /properties/{id}`).
- Handlers trim optional strings (`?.Trim()`), return `Results.ValidationProblem` for missing required fields, `Results.BadRequest("message")` for invalid cross-entity references, `NotFound` for missing route entities, and `Created($"/api/...", dto)` on POST. Status-code re-execution to `/not-found` is applied only to non-`/api` paths (`Program.cs`), so API clients get raw status codes.
- Asset history: endpoints that create, move, revalue or (un)archive an asset must also add the matching `AssetEvent` (`Added`, `Moved` via `RecordMove`, `Valued` when `CurrentValue` changes, `Archived`/`Unarchived`). Only `AssetEvent.ManualKinds` can be posted or deleted through `/assets/{id}/events`.
- Maintenance: `IntervalValue` + `IntervalUnit` (`days`/`months`/`years`, both null for one-off tasks). `POST /maintenance/{id}/complete` adds a record and sets `DueOn = NextDue(completedOn)` (null for one-offs), unless the completion is older than `LastCompletedOn` (back-filled history doesn't move the schedule). Tasks follow their fixture's property when a fixture or room moves, and a room holding assets can't move to another property.
- Currency is a per-property 3-letter ISO code normalized by `NormalizeCurrency` (blank → `"USD"`, invalid → `null` → validation error). Never sum money across currencies: the dashboard returns one `CurrencyTotalDto` per currency.

## Migrations

- `dotnet-ef` 10.0.10 is pinned in `dotnet-tools.json` (`dotnet tool restore`). `InventoryDbContextModelSnapshot.cs` exists as of `20261004160308_AlignRoomsAndAddAssetExternalId`, so scaffold new migrations normally with `dotnet ef migrations add`.
- The six migrations before that were hand-written, without designer files. Leave them alone.
- SQLite can't add foreign keys or alter columns in place. EF turns those operations into a table rebuild (`ef_temp_*`), which needs the migration's `.Designer.cs`. Always keep the generated designer file, and review the SQL with `dotnet ef migrations script`.
- When a migration needs data changes (`migrationBuilder.Sql`), remember that EF's SQLite provider runs table rebuilds (`AlterColumn`, `DropColumn`, `AddForeignKey`) *after* the SQL operations in the same migration, and logs a warning about it. Put data fixes that must precede the schema change in the same migration (see `SchemaCleanupAndAssetExtras`, which also shows how to generate GUIDs in SQL in EF's uppercase text format), and test against a seeded database of the previous schema.
- Every relationship or column change needs a migration. The app migrates the user's real database on startup, so test a migration against a **copy** of `inventory.db` (`dotnet ef database update --connection "Data Source=<copy>"`) before running the app.

## Import/export contract

- Backups use `InventoryExport` with `schemaVersion: 1` and string external IDs. Export writes `ExternalId ?? Id` for every record (`Ext()` in `Export`), and child references use the parent's exported ID, so backup → restore → backup cycles keep the same IDs. Children refer to parents through `*ExternalId` fields.
- Exported: properties, floors, rooms, surfaces, storage locations, active assets (and their photos), property photos, fixtures, fixture photos, room photos, paints, room-paint assignments (external ID `"{roomId}:{paintId}"`, since the table has a composite key). The paint library is global, so imported paints that match an existing paint are reused.
- Full backups are ZIPs (`GET /api/backup`: `inventory.json` + `files/<key>` for every referenced uploaded file, built in a temp file because `ZipArchive` writes synchronously). `POST /api/import/zip` reads the raw body (limit raised to 1 GB through `IHttpMaxRequestBodySizeFeature`), restores files with `FileStore.RestoreAsync` (valid key, size and sniffed type must match, existing files skipped) and returns `ZipImportDto` for the normal preview/confirm.
- The client always calls `POST /api/import/preview` before `POST /api/import/confirm` (`{ inventory, skipExternalIds }`). Confirm re-runs preview, then inserts everything in one transaction. Import is **idempotent by backup ID**. Confirm seeds each type's ID map with `ExistingIds()` (existing rows keyed by `ExternalId` and by database `Id`), skips any record already present, and attaches new children to existing parents. Restoring the same backup twice adds nothing, and a newer backup adds only new records. Preview reports `ExistingRecords` and lists matching assets in `DuplicateExternalIds` (still honoured as `skipExternalIds`). Paints also match on brand + colour name + code. Room-paint links are skipped when the room/paint pair exists. Storage locations are inserted parent-first, and preview rejects missing parents and cycles.
- Every collection after `PropertyPhotos` (`Fixtures`, `AssetPhotos`, `Paints`, `RoomPaints`, `RoomPhotos`, `FixturePhotos`) is optional (nullable) on `InventoryExport` for backward compatibility. Keep new collections optional the same way. When you change the schema, update `Export`, `Preview` and the confirm handler together.

## When changing things

- For a new or changed endpoint, update `Contracts.cs`, `InventoryApi.cs`, the client page, the README API table, and add or extend a test.
- Destructive buttons go through `ConfirmDialog`. Pages hold a `PendingConfirmation? pending` and run it from `OnConfirm`.
- Known bugs and planned modules are tracked in `documents/OUTSTANDING_WORK.md`. Check it before starting work and update it when you fix something.
