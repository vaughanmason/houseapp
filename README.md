# Home Inventory

Local-only property management app built with ASP.NET Core Minimal APIs, EF Core/SQLite, and Blazor WebAssembly. The current data model is a property-centric hierarchy of Property → Floor → Room → Surface, with nested storage locations, assets, dashboard metrics, photo metadata, room paint assignments, and JSON export/import backups.

## Run locally

```powershell
dotnet restore .\HomeInventory.sln
dotnet build .\HomeInventory.sln
dotnet run --project .\HomeInventory
```

Open the localhost URL shown by the application. The database lives in `%LOCALAPPDATA%\HomeInventory\inventory.db`; it is intentionally outside the repository. If you want a clean slate when upgrading from earlier builds, delete that file and restart the app—the app will recreate it and apply the latest migrations.

## What’s implemented

- Properties with address/purchase metadata and a configurable property-level currency (USD, EUR, GBP, etc.)
- Floors owned by properties
- Rooms owned by floors with area/volume, finish notes, window/door counts, utilities/fixtures notes, and paint assignments
- Surfaces attached to rooms with type-based metadata (wall, ceiling, flooring, trim)
- Nested storage location hierarchy with server-computed paths (`Parent → Child`) per property
- Assets with categories, brand/model/serial numbers, valuation, condition, archive toggle, and optional room/storage-location placement
- Fixtures scoped to rooms, with type-based metadata (e.g. manufacturer, model, serial number, warranty, installation/maintenance dates)
- Photo metadata registry for external storage references on properties, rooms, fixtures, and assets
- Dashboard summary metrics and category totals, including fixture and photo-backed inventory context
- Text search across asset names/categories/brands/serial numbers and fixture names/types
- JSON export/import using versioned schema (v1) with external IDs to prevent re-import collisions; preview validation before confirm transactional import
- **Enhanced UI with multi-page organization** for improved user experience

## UI Architecture

The application uses a multi-page, component-based architecture to organize functionality by entity type:

| Page | Purpose | Features |
|------|---------|----------|
| **Properties** | Manage properties and property photos | Add/edit properties with currency selection, manage property-level photos |
| **Floors** | Organize property floors | Create and manage floors by property with notes and sorting |
| **Rooms** | Rooms and surface management | Complete room metadata (dimensions, finishes, utilities), surface management (wall, ceiling, flooring, trim) |
| **Fixtures** | Permanent room fixtures | Fixture lifecycle (purchase, installation, maintenance), financial tracking, photos, warranty info |
| **Paints** | Paint library and assignments | Global paint registry, assign colors to room surfaces with installation date and notes |
| **Assets & Storage** | Assets and storage organization | Asset catalog with valuation and location tracking, storage location hierarchy, asset search/filter, photo management |

### Reusable Components

- **CurrencyDisplay** – Format monetary values with currency codes
- **Breadcrumb** – Navigation path context (foundation for future multi-level navigation)
- **ConfirmDialog** – Delete confirmation dialogs with customizable messaging
- **PropertySelector** – Property dropdown for filtering entity lists by property

## API Endpoints (`/api`)

| Resource | Methods | Notes |
|----------|---------|-------|
| Properties | GET, POST, PUT/{id} | Ownership boundary for floors/assets hierarchy |
| Floors | GET/POST/PUT/DELETE under properties or `/floors` | Floors belong to a property and own rooms |
| Rooms | GET by floor/property, POST, PUT/{id}, DELETE | Rooms now live under floors |
| Surfaces | GET/POST under `/rooms/{roomId}/surfaces`, PUT/DELETE via `/surfaces/{id}` | Surface type metadata is stored per room |
| Paints (library) | GET/POST/PUT/DELETE paints; `rooms/{roomId}/paints` assign colours to room surfaces | Registry-first approach with room-specific mappings |
| StorageLocations | GET by property, POST/PUT tree edits | Self-referencing hierarchy scoped per property |
| Assets | GET list/filter/archive, CRUD, move endpoint, archive toggle, photo CRUD | Cross-entity validation is enforced for property/floor/room references |
| Fixtures | GET/POST/PUT/DELETE under `/rooms/{roomId}/fixtures` and `/fixtures` | Room-scoped permanent items with type-specific metadata |
| FixturePhotos | GET/POST/DELETE under `/fixtures/{id}/photos` | External photo references for fixtures |
| AssetPhotos | GET/POST/DELETE under `/assets/{id}/photos` | External photo references for assets |

## Import Contract (`schemaVersion: 1`)

```csharp
InventoryExport {
  Properties[],
  Floors[],
  Rooms[],
  Surfaces[],
  StorageLocations[],
  Assets[],
  PropertyPhotos[],
  Fixtures[],
  AssetPhotos[]
}

ImportPreviewDto {
  IsValid,
  Errors[],
  Counts...,
  DuplicateExternalIds[]
}
```

All import/export records are linked by external IDs rather than database IDs, and the confirmation step imports inside a transaction after preview validation. Property imports can optionally include a `currency` field (3-letter ISO code); when omitted, the app defaults to `USD`.