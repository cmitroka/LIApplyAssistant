# CJM Apply Assistant / JobGrid

Shared SQLite job store (Azure Files) with a SQL relay API. Primary UI is the **Angular JobGrid SPA** (`web/`). A Windows Forms app remains for local experiments but is not the focus.

## What it does

1. Paste LinkedIn-style job card text into **Paste & Parse**.
2. Server-side parse extracts **Job Title**, **Company**, **Location**, **Viewed**, **Posted** text/date, **Top applicant**, and compensation when present.
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
| `POST /api/parse` | Body `{ "text": "..." }` → `{ jobs, count }` |
| `POST /api/jobs/import` | Body `{ "jobs": [ ... ] }` → `{ added, skipped, errors, total }` (`INSERT OR IGNORE`) |
| `POST /api/sql` | Universal SQL relay `{ "sql", "parameters" }` |
| Auth | Header `X-Api-Key` or `Authorization: Bearer …` (`CJM_APPLY_API_KEY`); empty key = open (local) |

CORS is enabled for the SPA.

### Local API

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
$env:CJM_APPLY_DB = "..\Data\cjm-apply.db"
$env:CJM_APPLY_API_KEY = ""
dotnet run --no-launch-profile --urls http://127.0.0.1:5088
```

Deploy:

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
.\deploy.ps1 -ApiKey "pick-a-long-secret"
```

## Angular SPA (`web/`)

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\web
npm start
```

Opens `http://localhost:4200`. Dev API base URL is `http://127.0.0.1:5088` (`src/environments/environment.development.ts`). Set `apiKey` there if the API requires one.

Routes:

- `/` — Paste & Parse (ingest)
- `/jobs` — Job Grid stub (behavior TBD)

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
