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

- Properties with address/purchase metadata
- Floors owned by properties
- Rooms owned by floors with area/volume, finish notes, window/door counts, utilities/fixtures notes, and paint assignments
- Surfaces attached to rooms with type-based metadata (wall, ceiling, flooring, trim)
- Nested storage location hierarchy with server-computed paths (`Parent → Child`) per property
- Assets with categories, brand/model/serial numbers, valuation, condition, and archive toggle
- Photo metadata registry for external storage references on properties and rooms
- Dashboard summary metrics and category totals
- Text search across asset names/categories/brands/serial numbers
- JSON export/import using versioned schema (v1) with external IDs to prevent re-import collisions; preview validation before confirm transactional import

## API Endpoints (`/api`)

| Resource | Methods | Notes |
|----------|---------|-------|
| Properties | GET, POST, PUT/{id} | Ownership boundary for floors/assets hierarchy |
| Floors | GET/POST/PUT/DELETE under properties or `/floors` | Floors belong to a property and own rooms |
| Rooms | GET by floor/property, POST, PUT/{id}, DELETE | Rooms now live under floors |
| Surfaces | GET/POST under `/rooms/{roomId}/surfaces`, PUT/DELETE via `/surfaces/{id}` | Surface type metadata is stored per room |
| Paints (library) | GET/POST/PUT/DELETE paints; `rooms/{roomId}/paints` assign colours to room surfaces | Registry-first approach with room-specific mappings |
| StorageLocations | GET by property, POST/PUT tree edits | Self-referencing hierarchy scoped per property |
| Assets | GET list/filter/archive, CRUD, move endpoint, archive toggle | Cross-entity validation is enforced for property/floor/room references |

## Import Contract (`schemaVersion: 1`)

```csharp
InventoryExport {
  Properties[],
  Floors[],
  Rooms[],
  Surfaces[],
  StorageLocations[],
  Assets[],
  PropertyPhotos[]
}

ImportPreviewDto {
  IsValid,
  Errors[],
  Counts...,
  DuplicateExternalIds[]
}
```

All import/export records are linked by external IDs rather than database IDs, and the confirmation step imports inside a transaction after preview validation.