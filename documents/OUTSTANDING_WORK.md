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

- **Import doesn't merge edits.** Records that already exist are skipped, not updated, so restoring an older backup never overwrites newer data, but it also can't push field changes into existing records.
- **Assets**: the search box on the Assets page only filters on the client (the Home page search uses the API).
- **Reusable components**: `Breadcrumb` isn't used on any page. `PropertySelector`/`ConfirmDialog` are used only on some pages.
- **Fixtures/assets** can't be moved between properties from the UI except through the asset edit form.

Resolved on 2026-10-04: property delete (blocked while it has assets, with a typed-name confirm), surface add/edit with type-specific fields, room photo UI, storage location rename/re-parent, the asset Move picker, the "Used in" paint view (`GET /api/paints/{id}/usage`), search over paints and surfaces, and a Rooms-page bug where saving a room wiped its paint details. Also resolved: the printable insurance report (module 14), asset history (module 13), utilities as a fixture category (module 11), full ZIP backup and restore including uploaded files, document and photo uploads (FileStore, Documents page, shared PhotoManager, warranty/receipt dashboard counts), the Maintenance module (tasks, service history, dashboard counts, backup support), a guard stopping rooms that hold assets from moving to another property, and idempotent imports (`ExternalId` on every importable entity, migration `AddExternalIdsToImportedEntities`), and storage locations import in any order, with missing parents and cycles rejected.

## P3 – Planned modules not yet started (from the product plan)

| Module | Notes |
|--------|-------|
| 6 – Home Inventory extras | Barcode, QR, receipt, manual and warranty fields on assets |
| 8 – Documents | Done, except OCR text (planned with the AI features) |
| 16 – QR codes | Generate and print labels for storage locations/assets, plus deep links |
| 17 – Dashboard | Maintenance, expiring warranties and missing receipts are done. Still to do: recent purchases, room completion (see the TODO in the `/dashboard` handler) |
| AI features | Photo recognition, receipt OCR, paint recognition, natural-language questions |
| Data model | Contacts (supplier/installer), Manufacturer and Category entities |

## Tech debt and housekeeping

- `Room.FloorId` is still nullable even though every room needs a floor. `Room.PropertyId` is denormalized from the floor and has to be kept in sync by the API.
- Fixtures still have free-text `MaintenanceSchedule`/`LastMaintenanceDate` fields from before the Maintenance module. Consider migrating them into tasks and removing them.
- ZIP backups are buffered in browser memory when restoring (Blazor WASM uploads), so very large backups (hundreds of MB) may be slow.
- Files uploaded but never attached (form abandoned) stay on disk, so a periodic orphan sweep would reclaim them.
- No image thumbnails are generated; full-size images are scaled in the browser. HEIC photos can be stored but most browsers can't display them.
- Maintenance service records can't carry photos directly. Attach a document to the task instead.
- Windows Application Control blocked the freshly built `HomeInventory.exe` on this machine, so the app had to run via `dotnet HomeInventory.dll`. Setting `<UseAppHost>false</UseAppHost>` would make `dotnet run` avoid the exe.
- Most nav-menu icon classes (`bi-tools-nav-menu`, `bi-palette-nav-menu`, and others) have no CSS rule, so those menu items show no icon. Only house, list, plus and calendar are defined in `NavMenu.razor.css`.
- `/api/search` and the asset/fixture DTO helpers load whole tables into memory. That's fine at household scale, but it won't scale.
- `AssetDtos`, `ValidateAsset` and the import confirm handler are still dense one-liners. Split them when you touch them.
- Test coverage: 36 API tests.
- Asset history starts when this feature shipped: assets created before 2026-10-04 have no "Added" event, and edits to fields other than location (e.g. value) aren't logged automatically. Use a manual "Valued" entry. Nothing yet covers paint CRUD or the property/room photo endpoints, and there are no UI (bUnit/Playwright) tests.
- No `.editorconfig` or extra analyzers.
- The product plan suggests Flutter/offline-first and cloud sync. The current implementation is Blazor WASM, local-only. That needs a deliberate decision before multi-device work starts.

Resolved: the template pages, placeholder test and stale TODO were removed, GitHub Actions CI was added (build with `-warnaserror` + tests), the model snapshot and the `dotnet-ef` local tool were added, the unused server `FormatMoney` and the duplicate on the Home page were removed, the CS8602 warning was fixed (the build is warning-free), `Export`/`Preview` were made readable, and `.github/copilot-instructions.md` was brought up to date.

## Suggested order

1. ~~Tidy-up and CI~~ (done).
2. ~~Close the P2 UI gaps~~ (done). ~~Backup IDs on every entity~~ (done: imports are idempotent).
3. ~~Maintenance (module 10)~~ and ~~documents/photo uploads (8/9)~~ (done). Remaining modules, using the agreed defaults:
   - QR codes: printable labels now, network access later.
   - AI features: last.
   - Stack: stay on Blazor, local-only.
