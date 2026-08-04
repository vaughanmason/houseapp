# Home Inventory Copilot Instructions

## Planning artifacts

- Store all plans in `.github\Plans\`.

## Commands

```powershell
dotnet restore .\HomeInventory.sln
dotnet build .\HomeInventory.sln
dotnet run --project .\HomeInventory
```

The server launch profile uses `https://localhost:7060` and `http://localhost:5068`. There is currently no test project, test runner, linter, or single-test command configured.

## Architecture

- `HomeInventory` is the ASP.NET Core host. It serves the Blazor WebAssembly client, exposes the `/api` minimal API group, and applies EF Core migrations during startup.
- `HomeInventory.Client` is the interactive WebAssembly UI. Pages call relative `/api` endpoints with `HttpClient`; the host registers the client assembly in `Program.cs` and renders it through `Components/App.razor`.
- Server persistence is SQLite through `InventoryDbContext` in `HomeInventory\Domain.cs`. The database is deliberately local-only at `%LOCALAPPDATA%\HomeInventory\inventory.db`, not in the repository.
- API wire records live in `HomeInventory.Client\Contracts.cs`, even though the server consumes them through its project reference. Keep those records and endpoint request/response shapes synchronized with client pages and `HomeInventory\InventoryApi.cs`.
- `InventoryApi.MapInventoryApi` contains the full application API: properties, rooms, nested storage locations, assets, dashboard/search, and JSON export/import. It is intentionally a consolidated minimal-API module rather than controllers or service classes.

## Data and API conventions

- A property is the ownership boundary. Rooms, storage locations, and assets must reference the same existing property; validate that invariant in any new or updated endpoint.
- Storage locations form a self-referencing tree scoped to one property. `StorageLocationDto.Path` is computed server-side with the `Parent → Child` display format. Preserve this behavior for API consumers rather than reconstructing paths in the client.
- Assets are archived with `IsArchived`; dashboard, search, default asset listings, and export all operate on active assets only. Do not replace archiving with deletion.
- EF relationships cascade only when a property is deleted. Room, storage-location parent, and asset location relationships are restrictive, so model changes that affect those relationships require a corresponding migration under `HomeInventory\Migrations`.
- Endpoint handlers trim optional text fields, return `Results.ValidationProblem` for required-field validation where established, and use `Results.BadRequest` for invalid cross-entity references. Maintain these response patterns for existing resource types.

## Import/export contract

- Backups use `InventoryExport` with `schemaVersion: 1` and external IDs, not database IDs. Child records refer to parent records through the relevant `*ExternalId` fields.
- Import always calls `/api/import/preview` before `/api/import/confirm`; confirmation imports inside a transaction and skips duplicate active assets by external ID supplied from the preview result. Keep preview validation and confirm behavior aligned when changing this schema.
