# CJM Apply Assistant / JobGrid

Shared SQLite job store (Azure Files) with a SQL relay API. Primary UI is the **Angular JobGrid SPA** (`web/`). A Windows Forms app remains for local experiments but is not the focus.

## What it does

1. Paste LinkedIn job-search **HTML** (Save Page / outerHTML) into **Paste & Parse** — plain text still works as a fallback.
2. Server-side parse finds `job-card-component-ref-{id}` cards and extracts **Job Title**, **Company**, **Location**, **Viewed**, **Posted**, **Top applicant**, compensation, and **Apply URL** (`/jobs/view/{id}`).
3. Build a deterministic **JobKey** = alphanumeric-only from `Title + Company + Location` (primary key; first insert wins).
4. Review/edit rows in the preview grid, check the ones to keep, click **Add row** to insert.
5. Duplicates (same JobKey) are skipped; a modal reports how many were added vs skipped.

## Shared SQLite on Azure

| Piece | Value |
|---|---|
| Storage account | `cjmpipelinefiles` |
| File share | `pipeline-data` |
| DB file | `cjm-apply.db` |
| Path inside Container Apps | `/mnt/pipeline/cjm-apply.db` |

Schema: `Sql/CreateSqlite.sql`. Local copy lives under `Data/cjm-apply.db`.

**Important:** SQLite on a file share needs a **single writer replica** (`maxReplicas: 1`) for the API container.

## API (`CjmApplyApi`)

| Endpoint | Purpose |
|---|---|
| `GET /api/health` | Liveness + DB path |
| `POST /api/parse` | Body `{ "text": "..." }` → `{ jobs, count, source }` (`html` or `text`) |
| `POST /api/jobs/import` | Body `{ "jobs": [ ... ] }` → `{ added, skipped, errors, total }` (`INSERT OR IGNORE`) |
| `GET /api/jobs` | List saved jobs (`?take=500`) |
| `POST /api/jobs/{jobKey}/applied/toggle` | Toggle `Applied` + set/clear `AppliedAt` |
| `POST /api/jobs/{jobKey}/ignored/toggle` | Toggle `Ignored` + set/clear `IgnoredAt` |
| `POST /api/sql` | Universal SQL relay `{ "sql", "parameters" }` |
| Auth | Header `X-Api-Key` or `Authorization: Bearer …` (`CJM_APPLY_API_KEY`); empty key = open (local only — avoid with the SPA) |

CORS is allow-listed via `Cors:Origins` (Development defaults to `http://localhost:4200`). Production ships with an empty list (no browser CORS) unless you set origins explicitly. `/api/sql` is for trusted non-browser clients; do not put the API key in the SPA bundle.

### Local API

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
$env:ASPNETCORE_ENVIRONMENT = "Development"   # loads Cors:Origins for the SPA
$env:CJM_APPLY_DB = "..\Data\cjm-apply.db"
$env:CJM_APPLY_API_KEY = "dev-key"
dotnet run --no-launch-profile --urls http://127.0.0.1:5088
```

Deploy (Docker build context is the **repo root** so `CjmApply.Core` is included):

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
.\deploy.ps1 -ApiKey "pick-a-long-secret"
```

## Angular SPA (`web/`)

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\web
npm install
npm start
```

Opens `http://localhost:4200`. Dev API base URL is `http://127.0.0.1:5088` (`src/environments/environment.development.ts`). Set matching `apiKey` there for local keyed API access.

Routes:

- `/` — Paste & Parse (ingest)
- `/jobs` — Job Grid (saved jobs, sort, Applied toggle)

## Solution layout

| Project | Role |
|---|---|
| `CjmApply.Core` | Shared `JobRecord`, LinkedIn paste parser, JobKey builder |
| `CjmApplyApi` | HTTP API (parse, import, SQL) |
| `CjmApplyAssistant` | Windows Forms (optional) |
| `web` | Angular JobGrid SPA |

## Typical workflow

1. Copy job cards from LinkedIn search results.
2. Paste into JobGrid → **Parse extract**.
3. Fix mis-parsed fields in the preview grid.
4. Check rows → **Add row** → confirm added/skipped in the modal.
5. Separate multiple cards with a line of `---` if needed.
