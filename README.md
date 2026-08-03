# Home Inventory

Local-only household inventory built with ASP.NET Core Minimal APIs, EF Core/SQLite, and Blazor WebAssembly.

## Run locally

```powershell
dotnet run --project .\HomeInventory
```

Open the localhost URL shown by the application. The database lives in `%LOCALAPPDATA%\HomeInventory\inventory.db`; it is intentionally outside the repository. Use **Import & backup** to download a portable JSON backup.

## Included MVP workflows

- Properties and rooms
- Nested storage locations
- Asset catalogue, valuation dashboard, search, and archive
- JSON export and validated/import-preview workflow for ChatGPT-generated inventory

Attachments, barcodes, mobile camera capture, accounts, and cloud sync are deliberately deferred.
