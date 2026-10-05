# Outstanding Work

_Last updated: 2026-10-04._

This started as a code review of commit `3d4bb42` and has tracked the work since, compared against the [product plan](Home%20Inventory%20%26%20Property%20Management%20App%20%E2%80%93%20Detailed%20Plan.md). Everything that could be done without a product decision is done. What remains below either needs your decision or is a known limitation.

## Needs a decision

| Item | What's needed |
|------|---------------|
| **More AI features** | Receipt reading and "Fill from photo" are done. Paint colour recognition and plain-language questions ("Where is my drill?") remain; they need an agreed scope and cost. |
| **HTTPS on the home network** | Network access uses plain HTTP inside the home network. Live in-browser camera scanning and stronger protection against snooping on shared Wi-Fi would need HTTPS, which means a certificate trusted by each phone. |

## Decided

- **Defaults adopted (2026-10-05):** network access behind a shared PIN, home network only; Magick.NET for thumbnails and HEIC (SkiaSharp can't decode HEIC on Windows); photo-based barcode scanning with ZXing.Net (live camera scanning needs HTTPS); Claude for receipt reading and photo-to-asset; **restore merging stays as it is**: existing records are skipped, never overwritten.
- **Stack (2026-10-04):** stay on Blazor WebAssembly with the ASP.NET Core minimal-API host and SQLite. The Flutter/offline-first suggestion in the original plan is not being pursued. Phone and multi-device access will come through network access to this app (see above), not a separate client.

## Known limitations

- **Backup size:** restoring a ZIP backup goes through browser memory (Blazor WASM uploads), so backups of hundreds of MB may be slow.
- **Asset history:** existing assets were given a backfilled "Added" entry dated by purchase date (or the migration date). Only location and value changes are logged automatically; other edits aren't.
- **Suggestion lists:** manufacturers and categories are suggestions drawn from values already in use (`GET /api/lookups`), not managed lists with their own pages.
- **Contacts:** supplier and installer fields offer contact names as suggestions but store text, not a link to the contact record.
- **Room completion:** a room counts as fully documented with dimensions, flooring, wall finish, at least one surface and at least one photo. Change `RoomCompletion` in `InventoryApi.cs` if you'd like a different definition.
- **Maintenance:** service records can't carry photos directly. Attach a document to the task instead.
- **Tests:** there are 55 HTTP-level API tests but no browser UI tests (bUnit/Playwright). Windows Smart App Control blocks the unsigned test assemblies on the dev machine, so tests run in CI (every branch).
- **HEIC from old backups:** HEIC files restored from a backup stay HEIC (thumbnails still work); only new uploads are converted.
- **AI cost:** each receipt or photo is a paid Claude request (roughly US$0.01-0.03). Requests are only made when you press the button.

## Done

### Bugs from the original review

| # | Fix |
|---|-----|
| 1–2 | Storage locations can be created (correct endpoint, parent picker) and deleted (rejected while they hold sub-locations or assets). |
| 3 | Floor and room deletes return 400 while assets are inside, instead of an FK 500. |
| 4 | Fixture updates check that the room exists. |
| 5 | Storage location cycles and cross-property moves with dependents are rejected (cycles used to overflow the stack). |
| 6 | Import no longer crashes on photos of skipped duplicate assets. |
| 7 | Backups include paints, room paints, room/fixture photos, and everything added since. |
| 8 | Assets can't be hard-deleted; archive/unarchive with an archived view. |
| 9 | Dashboard totals each currency separately. |
| 10 | Duplicate detection uses backup IDs. |
| 11 | Schema drift fixed: `Rooms.FloorId` FK added, `Room.FloorId` now required, `Room.PropertyId` matches the database. |
| – | API 404s are no longer turned into 405s by the `/not-found` page. |

### Features

- Property delete (blocked while it has assets, typed-name confirmation); surface add/edit with type-specific fields; room photos; storage rename/re-parent; asset Move picker.
- Paint "Used in" view; search over assets, fixtures, storage, paints, surfaces, documents and contacts (filtered in SQL).
- Idempotent imports: backup IDs on every entity; storage locations import in any order; older backups with legacy fixture maintenance text are converted into tasks.
- Maintenance module (module 10): tasks on a property or fixture, recurring or one-off, service history, dashboard counts.
- Documents and photo uploads (modules 8–9): `FileStore`, Documents page, shared `PhotoManager`, warranty and missing-receipt dashboard counts, orphaned-file sweep.
- Full ZIP backup and restore including uploaded files.
- Utilities as a fixture category with provider and account number (module 11).
- Asset history (module 13): automatic Added/Moved/Valued/Archived/Unarchived entries plus manual Repaired/Serviced/Valued/Note entries.
- Printable insurance report (module 14).
- QR labels and scan pages with breadcrumbs (module 16, local only).
- Dashboard (module 17): recent purchases and room completion, alongside the value, maintenance, warranty and receipt cards.
- Asset barcode and manual link (module 6, minus scanning); contacts directory with suggestions in supplier/installer fields.
- Delete confirmations on every destructive action.
- Network access for phones behind a PIN; thumbnails and HEIC conversion; photo-based barcode scanning; Claude receipt reading (with searchable document text) and "Fill from photo".

### Housekeeping

- CI on GitHub Actions (Release build with `-warnaserror`, tests); .NET analyzers at `latest-recommended`; `.editorconfig`.
- EF model snapshot plus the pinned `dotnet-ef` local tool; every migration since then verified against a copy of a real database.
- `UseAppHost=false`, so `dotnet run` works where Windows Application Control blocks the unsigned exe.
- Legacy free-text fixture maintenance fields migrated into tasks and dropped.
- Template pages, placeholder test and dead code removed; dense helpers split; nav icons for every page.
