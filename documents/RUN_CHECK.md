# Run Check

_Performed 2026-10-04 on Windows 11 against commit `3d4bb42`._

## Result: ✅ The application builds, tests pass, and it runs.

| Step | Command | Result |
|------|---------|--------|
| SDK | `dotnet --list-sdks` | 10.0.112, 10.0.303 (project targets `net10.0`) |
| Build | `dotnet build .\HomeInventory.sln` | **Succeeded**, 0 errors, 1 warning (CS8602 at `InventoryApi.cs:636`) |
| Tests | `dotnet test .\HomeInventory.Tests` | **6 passed**, 0 failed (≈4 s) |
| Run | `dotnet run --project .\HomeInventory --launch-profile http` | Started, listening on `http://localhost:5068`. The existing local DB was already up to date ("No migrations were applied") |

Startup log note: `Model snapshot was not found in assembly 'HomeInventory'. Skipping pending model changes check.` This is expected given the hand-written migrations (see `OUTSTANDING_WORK.md` → Tech debt).

## Smoke test (HTTP status codes)

| Request | Status |
|---------|--------|
| `GET /` | 200 |
| `GET /properties`, `GET /assets` (client routes) | 200 |
| `GET /_framework/blazor.web.js` | 200 |
| `GET /api/properties` | 200 |
| `GET /api/floors` | 200 |
| `GET /api/locations` | 200 |
| `GET /api/assets?archived=false` | 200 |
| `GET /api/dashboard` | 200 |
| `GET /api/search?q=a` | 200 |
| `GET /api/export` | 200 |
| `POST /api/properties/{id}/locations` (used by the Assets page) | **400**: no such API route (bug #1) |
| `DELETE /api/locations/{id}` (used by the Assets page) | **405**: no such API route (bug #2) |

No records were written during the check. The browser UI wasn't exercised interactively.
