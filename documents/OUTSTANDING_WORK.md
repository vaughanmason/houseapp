# Outstanding Work

_Last reviewed: 2026-10-04 (commit `3d4bb42`). All P1 bugs found in that review are fixed; see [Fixed](#fixed)._

Findings from a full code review of `HomeInventory`, `HomeInventory.Client` and `HomeInventory.Tests`, compared against the [product plan](Home%20Inventory%20%26%20Property%20Management%20App%20%E2%80%93%20Detailed%20Plan.md). The two UI bugs marked ✅ were confirmed against the running app (see [RUN_CHECK.md](RUN_CHECK.md)).

## P1 – Bugs

None open.

## Fixed

| # | Fix | Test |
|---|-----|------|
| 1 ✅ | Assets page POSTs to `api/locations`. Also added a parent-location picker, and the "Notes" box is now labelled "Type" to match the field it saves. | `StorageLocations_CreateNested_ComputesPathAndDeletesLeafFirst` |
| 2 ✅ | Added `DELETE /api/locations/{id}`. It rejects locations that still have sub-locations or assets. | same, plus `StorageLocation_Delete_WithAsset_ReturnsBadRequest` |
| 3 | Floor and room deletes return 400 while assets are still placed in them (archived assets included, since the FK applies to them too). | `DeleteRoomAndFloor_WithAssets_ReturnsBadRequestUntilAssetsMoved` |
| 4 | `PUT /fixtures/{id}` checks that the room exists. | `UpdateFixture_WithUnknownRoom_ReturnsBadRequest` |
| 5 | `PUT /locations/{id}` rejects moving a location under one of its own descendants (this used to overflow the stack) and rejects a property change while it still has children or assets. | `StorageLocation_Update_RejectsCycle`, `StorageLocation_Update_RejectsPropertyChangeWithChildren` |
| 6 | Import confirm skips asset photos that belong to skipped duplicate assets. | `ImportConfirm_SkippedDuplicateAssetWithPhotos_Succeeds` |
| 7 | Backups include paints, room-paint assignments, room photos and fixture photos as optional collections. Imported paints matching an existing brand/colour/code are reused. Export no longer includes photos of archived assets, which made re-importing your own backup fail preview. | `ExportThenImport_RoundTripsPaintsAndAllPhotos` |
| 8 | Assets can't be hard-deleted: the `DELETE /api/assets/{id}` endpoint and the UI button are gone. Added `POST /api/assets/{id}/unarchive` and a "Show archived" toggle on the Assets page. | `Assets_ArchiveAndUnarchive_NoHardDelete` |
| 9 | Dashboard returns one `CurrencyTotalDto` per property currency instead of summing mixed currencies. The Home page shows a value card and category table per currency. | `Dashboard_TotalsEachCurrencySeparately` |
| 10 | Import duplicate detection uses external IDs. `Asset.ExternalId` is stored on import and written back on export (`ExternalId ?? Id`), so backup → restore → backup cycles stay recognisable. The name + location heuristic was removed. | `ImportPreview_DetectsDuplicatesByExternalId` |
| 11 | Schema drift fixed. The migration `20261004160308_AlignRoomsAndAddAssetExternalId` rebuilds `Rooms` with the missing `FloorId → Floors` cascading FK and adds `Assets.ExternalId`. `Room.PropertyId` is non-nullable with its Property FK, matching the database. The migration was verified against a copy of the real database before being committed. | full migration chain runs in every test |
| – | API 404s were being re-executed through the `/not-found` Razor page, so `DELETE`/`PUT` 404s came back as 405. Status-code pages now apply to non-`/api` paths only. | covered by the location delete test |

## P2 – Functional gaps in the existing modules

- **Import de-duplication only covers assets.** Importing the same backup twice still creates a second copy of its properties, floors, rooms, locations, fixtures and photos. Only assets (and their photos) are skipped. Full idempotent restore would need `ExternalId` on every imported entity, or a "replace everything" restore mode.
- **Properties**: no `DELETE /api/properties/{id}`.
- **Rooms**: the surface edit endpoint exists (`PUT /surfaces/{id}`) but the Rooms page only supports add/delete. The room photo endpoints exist but have no UI.
- **Storage**: the UI can create nested locations but has no rename or move.
- **Assets**: the move endpoint (`POST /assets/{id}/move`) isn't used by the UI. The search box on the page only filters on the client.
- **Paints**: no "which rooms use this paint" view (plan: *Search "Blue Paint"*). Search doesn't cover paints or surfaces.
- **Reusable components**: `Breadcrumb` isn't used on any page. `PropertySelector`/`ConfirmDialog` are used only on some pages.

## P3 – Planned modules not yet started (from the product plan)

| Module | Notes |
|--------|-------|
| 6 – Home Inventory extras | Barcode, QR, receipt, manual and warranty fields on assets |
| 8 – Documents | Attach PDFs/images anywhere, tags, OCR text. Needs real file storage (none exists today) |
| 9 – Photos | Photo metadata exists, but there is no upload, storage or thumbnails |
| 10 – Maintenance | Recurring tasks with due date, last service, supplier, cost. Fixtures have only free-text `MaintenanceSchedule` |
| 11 – Utilities | Electrical panel, meters, solar, inverter, and similar. Only free-text `UtilitiesNotes` on Room today |
| 13 – Asset history | Audit trail of moves and repairs ("nothing gets deleted") |
| 14 – Insurance | Per-currency totals now exist on the dashboard. Still needs an insurance report and PDF export |
| 16 – QR codes | Generate and print labels for storage locations/assets, plus deep links |
| 17 – Dashboard | Due maintenance, expiring warranties, missing receipts, recent purchases, room completion (see the TODO in the `/dashboard` handler) |
| AI features | Photo recognition, receipt OCR, paint recognition, natural-language questions |
| Data model | Contacts (supplier/installer), Manufacturer and Category entities |

## Tech debt and housekeeping

- Stale TODO at the top of `MapInventoryApi`: floors, surfaces, fixtures, paint and photos are done.
- Template leftovers: `Pages/Counter.razor`, `Pages/Weather.razor`, `HomeInventory.Tests/UnitTest1.cs`.
- `Room.FloorId` is still nullable even though every room needs a floor. `Room.PropertyId` is denormalized from the floor and has to be kept in sync by the API.
- `/api/search` and the asset/fixture DTO helpers load whole tables into memory. That's fine at household scale, but it won't scale.
- `AssetDtos`, `ValidateAsset` and the import confirm handler are still dense one-liners. Split them when you touch them.
- Test coverage: 17 tests now, but nothing yet covers surfaces, paints CRUD, search, or the property/room photo endpoints.
- No CI pipeline, no `.editorconfig`, no analyzers.
- The product plan suggests Flutter/offline-first and cloud sync. The current implementation is Blazor WASM, local-only. That needs a deliberate decision before multi-device work starts.

Resolved: the model snapshot and the `dotnet-ef` local tool were added, the unused server `FormatMoney` and the duplicate on the Home page were removed, the CS8602 warning was fixed (the build is warning-free), `Export`/`Preview` were made readable, and `.github/copilot-instructions.md` was brought up to date.

## Suggested order

1. Tidy up: remove the template pages, `UnitTest1.cs` and the stale TODO, and add CI running `dotnet build` + `dotnet test`.
2. Close the P2 UI gaps (surface edit, room photos, storage rename/move, asset move) and decide on the import de-duplication scope for non-asset entities.
3. Then start new modules. Maintenance (10) and Documents/file storage (8/9) unlock most of the dashboard goals.
