# Home Inventory

Local-only household inventory built with ASP.NET Core Minimal APIs, EF Core/SQLite, and Blazor WebAssembly. Tracks properties → rooms → nested storage locations (cabinets/drawers) → assets with valuation dashboard, search, archive toggle, photo metadata registry, paint color library assignments by surface area (walls/flooring), document attachments for receipts/warranties, maintenance schedules, utilities/fixtures notes per room, JSON export/import backups using external IDs to prevent re-import collisions.

## Run locally

```powershell
dotnet run --project .\HomeInventory
```

Open the localhost URL shown by the application. The database lives in `%LOCALAPPDATA%\HomeInventory\inventory.db`; it is intentionally outside the repository. Use **Import & backup** → JSON export to download a portable backup, then restore via Import Preview → Confirm workflow.

## Features

- Properties with address/purchase metadata
- Rooms: name/type (bedroom/bath), area/volume measurements, surface finish notes for paint/fixture tracking, window/door counts, utilities/fixtures notes fields ready for normalization
- Standalone Paint library + room-specific wall/flooring/color assignments via registry-first approach  
- Nested storage location hierarchy with server-computed paths (`Parent → Child`) per property (cabinet/drawer/shelf trees)
- Assets: name/category (furniture/electronics/etc), brand/model/serial numbers, purchase price/current value depreciation tracking, condition ratings, archive toggle instead of hard-delete
- Photo metadata registry for external blob storage references with captions/sort order on properties and rooms  
- Dashboard summary metrics + category totals  
- Text search across asset names/categories/brands/SNs  
- JSON export/import using versioned schema (v1) with external IDs to prevent re-import collisions; preview validation before confirm transactional import

## API Endpoints (`/api`)

| Resource | Methods | Notes |
|----------|---------|-------|
| Properties | GET, POST, PUT/{id} | Ownership boundary for rooms/assets hierarchy |
| Rooms | GET by property, POST, PUT/{id}, DELETE via UI flag? | Surface notes ready for paint/fixtures modules |
| Paints (library) | GET/POST/DELETE paints; `rooms/{roomId}/paints` assign to walls/floors with sort order/surface type badges | Registry-first approach then room-specific mappings |
| StorageLocations | GET by property, POST/PUT tree edits | Self-referencing hierarchy scoped per property |
| Assets | GET list/filter/archive?, CRUD, MOVE location endpoint, archive toggle only | Cross-entity validation enforced everywhere (no orphan references) |

## Import Contract (`schemaVersion: 1`)

```csharp
InventoryExport { Properties[], Rooms[], StorageLocations[], Assets[] } // all nested by externalId references, not database IDs; skip archived on export/confirm
ImportPreviewDto { IsValid, Errors[], Counts..., DuplicateExternalIds[] }  // preview before confirm transactional import skipping duplicates by name + location equality checks
```

## TODO: Upcoming Modules (Pending Implementation)

- **Floors**: floorplan sketches/photos per room  
- **Surfaces/Fixtures**: window/door specs normalize into dedicated fixtures table, mounted on rooms entity notes fields pending normalization  
- **Maintenance schedules**, warranties, documents repository for receipts/warranties attachments  
- Paint swatches/thumbnails, color matching API endpoints, maintenance due date notifications