# Home Inventory

Local-only property management app built with ASP.NET Core Minimal APIs, EF Core/SQLite, and Blazor WebAssembly. The current data model is a property-centric hierarchy of Property → Floor → Room → Surface, with nested storage locations, assets, dashboard metrics, photo metadata, room paint assignments, and JSON export/import backups.

## Run locally

```powershell
dotnet restore .\HomeInventory.sln
dotnet tool restore   # dotnet-ef, for migrations
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
- Maintenance tasks for a property or one of its fixtures/utilities: one-off or repeating every N days/months/years, with service history (date, cost, supplier, notes) and automatic next-due dates
- Photo uploads (with thumbnails) on properties, rooms, fixtures and assets; links to externally stored files still work
- Documents (receipts, invoices, manuals, warranties, insurance, certificates, plans) uploaded as images or PDFs, attached to a property, room, fixture, asset or maintenance task, with dates, expiry, tags and notes
- Uploaded files live in `%LOCALAPPDATA%\HomeInventory\files` (images and PDFs only, 20 MB max, type checked from the file contents)
- Dashboard summary metrics, category totals grouped by property currency, overdue / due-in-30-days maintenance, warranties expiring within 90 days, and active assets missing a receipt
- Text search across assets, fixtures, storage locations, paints (with the rooms that use them) and surfaces
- JSON export/import using versioned schema (v1) with external IDs to prevent re-import collisions; preview validation before confirm transactional import
- **Enhanced UI with multi-page organization** for improved user experience

## UI Architecture

The application uses a multi-page, component-based architecture to organize functionality by entity type:

| Page | Purpose | Features |
|------|---------|----------|
| **Properties** | Manage properties and property photos | Add/edit properties with currency selection, delete (blocked while it has assets; type the name to confirm), manage property-level photos |
| **Floors** | Organize property floors | Create and manage floors by property with notes and sorting |
| **Rooms** | Rooms, surfaces and room photos | Complete room metadata (dimensions, finishes, paint details, utilities); add/edit surfaces with type-specific fields (paint for wall/ceiling/trim, product and supplier for flooring); room photos |
| **Fixtures** | Permanent room fixtures | Fixture lifecycle (purchase, installation, maintenance), financial tracking, photos, warranty info |
| **Maintenance** | Recurring and one-off jobs | Tasks grouped into Overdue / Due in 30 days / Upcoming / No due date; Mark done (advances the schedule), service history, edit, delete |
| **Documents** | Receipts, manuals, warranties… | Upload and attach to a property/room/fixture/asset/maintenance task; filter by property, kind and text; expiry badges; open, edit, delete |
| **Paints** | Paint library and assignments | Global paint registry, assign colours to rooms, "Used in" view listing every room using a paint |
| **Assets & Storage** | Assets and storage organization | Asset catalog with valuation, Move picker, archive/unarchive with an archived view; nested storage locations with edit/re-parent; photo management |

### Reusable Components

- **CurrencyDisplay** – Format monetary values with currency codes
- **Breadcrumb** – Navigation path context (foundation for future multi-level navigation)
- **ConfirmDialog** – Delete confirmation dialogs with customizable messaging, optional body content and a disabled-until-valid confirm button
- **PropertySelector** – Property dropdown for filtering entity lists by property

## API Endpoints (`/api`)

| Resource | Methods | Notes |
|----------|---------|-------|
| Properties | GET, POST, PUT/{id}, DELETE/{id} | Ownership boundary; DELETE cascades the structure and is rejected while the property has any assets |
| Floors | GET/POST/PUT/DELETE under properties or `/floors` | Floors belong to a property and own rooms |
| Rooms | GET by floor/property, POST, PUT/{id}, DELETE | Rooms now live under floors |
| Surfaces | GET/POST under `/rooms/{roomId}/surfaces`, PUT/DELETE via `/surfaces/{id}` | Surface type metadata is stored per room |
| RoomPhotos | GET/POST/PUT/DELETE under `/rooms/{roomId}/photos` | External photo references for rooms |
| Search | GET `/search?q=` | Assets, fixtures, storage, paints, surfaces (max 50 results) |
| Paints (library) | GET/POST/PUT/DELETE paints; `paints/{id}/usage`; `rooms/{roomId}/paints` assign colours to rooms | Usage combines assignments with painted surfaces matching the colour code, or the colour name and brand |
| StorageLocations | GET by property, POST/PUT tree edits, DELETE (empty leaf only) | Self-referencing hierarchy scoped per property; cycles rejected |
| Assets | GET list/filter (`archived=true/false`), POST, PUT, move, archive/unarchive (no hard delete), photo CRUD | Cross-entity validation is enforced for property/floor/room references |
| Fixtures | GET/POST/PUT/DELETE under `/rooms/{roomId}/fixtures` and `/fixtures` | Room-scoped permanent items with type-specific metadata |
| Maintenance | GET (`propertyId`, `fixtureId` filters), POST, PUT/{id}, DELETE/{id}, POST `/{id}/complete`, GET `/{id}/history` | Task belongs to a property and optionally one of its fixtures; completing sets the next due date from the completion date |
| Files | POST `/files` (multipart `file`), GET `/files/{key}` | Images (JPEG/PNG/GIF/WebP/HEIC) and PDFs up to 20 MB; returns a storage key for photos and documents |
| Documents | GET (property/room/fixture/asset/task/kind filters), POST, PUT/{id}, DELETE/{id} | Attached to at most one target in the same property; deleting the target keeps the document at property level |
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
  Fixtures[],       // optional
  AssetPhotos[],    // optional
  Paints[],         // optional; matched to existing paints by brand/colour/code
  RoomPaints[],     // optional
  RoomPhotos[],     // optional
  FixturePhotos[],  // optional
  MaintenanceTasks[],   // optional
  MaintenanceRecords[], // optional
  Documents[]           // optional
}

ImportPreviewDto {
  IsValid,
  Errors[],
  Counts...,
  DuplicateExternalIds[]
}
```

Backups contain document and photo details, not the files themselves: copy `%LOCALAPPDATA%\HomeInventory\files` alongside the JSON to keep them.

All import/export records are linked by external IDs rather than database IDs, and the confirmation step imports inside a transaction after preview validation. Every imported record remembers its backup ID, and exports reuse it. Restoring the same backup twice changes nothing, and restoring a newer backup only adds what is new. Storage locations may appear in any order. Property imports can optionally include a `currency` field (3-letter ISO code); when omitted, the app defaults to `USD`.