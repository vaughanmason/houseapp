# Outstanding Work

_Last reviewed: 2026-10-04 (commit `3d4bb42`)._

Findings from a full code review of `HomeInventory`, `HomeInventory.Client` and `HomeInventory.Tests`, compared against the [product plan](Home%20Inventory%20%26%20Property%20Management%20App%20%E2%80%93%20Detailed%20Plan.md). The two UI bugs marked ✅ were confirmed against the running app (see [RUN_CHECK.md](RUN_CHECK.md)).

## P1 – Bugs

| # | Area | Problem | Where |
|---|------|---------|-------|
| 1 | Storage UI ✅ | "Add storage location" POSTs to `api/properties/{id}/locations`, which doesn't exist. The server only has `POST /api/locations`, so the request falls through to the Razor endpoint and returns **400**. Storage locations can't be created from the UI. | [Assets.razor:337](../HomeInventory.Client/Pages/Assets.razor#L337) |
| 2 | Storage UI/API ✅ | The UI calls `DELETE api/locations/{id}`, but no delete endpoint exists, so it returns **405**. The endpoint should refuse to delete a location that has children or assets (both FKs are `Restrict`). | [Assets.razor:346](../HomeInventory.Client/Pages/Assets.razor#L346), [InventoryApi.cs:422-437](../HomeInventory/InventoryApi.cs#L422-L437) |
| 3 | Floor/room delete | Floor → Room cascades, but `Asset.RoomId` is `Restrict`. Deleting a floor or room that holds assets throws an unhandled FK exception (500). The endpoint should check first and return `BadRequest` (or unassign the assets). | [InventoryApi.cs:67-74](../HomeInventory/InventoryApi.cs#L67-L74), [146-153](../HomeInventory/InventoryApi.cs#L146-L153), [Domain.cs:272,283](../HomeInventory/Domain.cs#L272) |
| 4 | Fixture update | `PUT /fixtures/{id}` assigns `input.RoomId` without checking that the room exists, which causes an FK 500. | [InventoryApi.cs:279](../HomeInventory/InventoryApi.cs#L279) |
| 5 | Storage tree | `PUT /locations/{id}` only rejects `ParentId == id`. Deeper cycles (A→B→A) make the recursive `Path()` overflow the stack and take down every location, asset and search request. Changing `PropertyId` on a location with children leaves the children in the old property. | [InventoryApi.cs:435](../HomeInventory/InventoryApi.cs#L435), [641](../HomeInventory/InventoryApi.cs#L641) |
| 6 | Import confirm | Asset photos whose asset is in `SkipExternalIds` throw `KeyNotFoundException` (`assets[photo.AssetExternalId]`). Importing a backup that has a duplicate asset with photos fails. | [InventoryApi.cs:604](../HomeInventory/InventoryApi.cs#L604) |
| 7 | Backup fidelity | Export/import leaves out **paints, room-paint assignments, room photos and fixture photos**, so a backup + restore loses data. Needs optional collections added to `InventoryExport` plus `Export`/`Preview`/confirm support. | [Contracts.cs:33](../HomeInventory.Client/Contracts.cs#L33), [InventoryApi.cs:645](../HomeInventory/InventoryApi.cs#L645) |
| 8 | Archive convention | The Assets page has a hard **Delete** button (`DELETE /api/assets/{id}`), which goes against the archive-only rule. There is no unarchive endpoint and no UI for viewing archived assets. | [Assets.razor:316](../HomeInventory.Client/Pages/Assets.razor#L316), [InventoryApi.cs:459-486](../HomeInventory/InventoryApi.cs#L459-L486) |
| 9 | Dashboard currency | Values from every property are summed regardless of currency and labelled with the alphabetically first property's currency. It should group totals by currency or filter by property. | [InventoryApi.cs:510-522](../HomeInventory/InventoryApi.cs#L510-L522) |
| 10 | Import duplicates | Preview flags duplicates by name + location path, not external ID. Re-importing the same backup into a different location tree won't be detected, and two different assets with the same name in the same place are wrongly skipped. The behavior and the docs should agree. | [InventoryApi.cs:646](../HomeInventory/InventoryApi.cs#L646) |

## P2 – Functional gaps in the existing modules

- **Properties**: no `DELETE /api/properties/{id}`.
- **Rooms**: the surface edit endpoint exists (`PUT /surfaces/{id}`) but the Rooms page only supports add/delete. The room photo endpoints exist but have no UI.
- **Storage**: the UI can only create top-level locations. There is no parent picker and no rename/move in the UI.
- **Assets**: the move endpoint (`POST /assets/{id}/move`) isn't used by the UI. The search box on the page only filters on the client.
- **Import page**: the help text lists only `properties, rooms, storageLocations, assets, propertyPhotos` and is missing floors, surfaces, fixtures and assetPhotos. Preview counts don't include fixtures or photos.
- **Paints**: no "which rooms use this paint" view (plan: *Search "Blue Paint"*). Search doesn't cover paints or surfaces.
- **Reusable components**: `Breadcrumb` isn't used on any page. `PropertySelector`/`ConfirmDialog`/`CurrencyDisplay` are used only on some pages.

## P3 – Planned modules not yet started (from the product plan)

| Module | Notes |
|--------|-------|
| 6 – Home Inventory extras | Barcode, QR, receipt, manual and warranty fields on assets |
| 8 – Documents | Attach PDFs/images anywhere, tags, OCR text. Needs real file storage (none exists today) |
| 9 – Photos | Photo metadata exists, but there is no upload, storage or thumbnails |
| 10 – Maintenance | Recurring tasks with due date, last service, supplier, cost. Fixtures have only free-text `MaintenanceSchedule` |
| 11 – Utilities | Electrical panel, meters, solar, inverter, and similar. Only free-text `UtilitiesNotes` on Room today |
| 13 – Asset history | Audit trail of moves and repairs ("nothing gets deleted") |
| 14 – Insurance | Totals by category per currency plus a PDF export |
| 16 – QR codes | Generate and print labels for storage locations/assets, plus deep links |
| 17 – Dashboard | Due maintenance, expiring warranties, missing receipts, recent purchases, room completion (see the TODO at [InventoryApi.cs:512](../HomeInventory/InventoryApi.cs#L512)) |
| AI features | Photo recognition, receipt OCR, paint recognition, natural-language questions |
| Data model | Contacts (supplier/installer), Manufacturer and Category entities |

## Tech debt and housekeeping

- **No EF model snapshot or designer files.** Migrations are hand-written, so `dotnet ef migrations add` would scaffold the whole schema again. Either generate a snapshot that matches the current model or keep hand-writing migrations (documented in `CLAUDE.md`). `dotnet-ef` isn't installed. Consider adding a local tool manifest.
- Stale TODO at [InventoryApi.cs:13](../HomeInventory/InventoryApi.cs#L13): floors, surfaces, fixtures, paint and photos are done.
- Unused `FormatMoney` in [InventoryApi.cs:660](../HomeInventory/InventoryApi.cs#L660). `Home.razor` has its own copy instead of using `CurrencyDisplay`.
- Nullable warning CS8602 at [InventoryApi.cs:636](../HomeInventory/InventoryApi.cs#L636).
- Template leftovers: `Pages/Counter.razor`, `Pages/Weather.razor`, `HomeInventory.Tests/UnitTest1.cs`.
- `Room.PropertyId` and `Room.FloorId` are nullable even though every room needs a floor. `PropertyId` is denormalized and can drift.
- `/api/search` and the asset/fixture DTO helpers load whole tables into memory. That's fine at household scale, but it won't scale.
- Very dense one-line methods (`Export`, `Preview`, `AssetDtos`, `ValidateAsset`) are hard to review. Split them when you touch them.
- Test coverage is thin: 5 real tests (properties, rooms, fixtures/photos, asset archive, import). Nothing covers floors, surfaces, paints, storage locations, move, search, dashboard, or the validation/failure paths above.
- No CI pipeline, no `.editorconfig`, no analyzers.
- `.github/copilot-instructions.md` was out of date. It was corrected alongside the new `CLAUDE.md`.
- The product plan suggests Flutter/offline-first and cloud sync. The current implementation is Blazor WASM, local-only. That needs a deliberate decision before multi-device work starts.

## Suggested order

1. Fix bugs 1–2 (the storage UI is unusable) and 5 (stack overflow risk), each with tests.
2. Fix bugs 3, 4 and 6 (unhandled 500s), each with tests.
3. Bring backups to full fidelity (7) and decide on duplicate semantics (10).
4. Archive vs. delete in the UI (8) and the dashboard currency (9).
5. Tidy up: remove template pages, stale TODO and warning, and add CI running `dotnet build` + `dotnet test`.
6. Then start new modules. Maintenance (10) and Documents/file storage (8/9) unlock most of the dashboard goals.
