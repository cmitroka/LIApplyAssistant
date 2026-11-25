# CJM Apply Assistant

Local Windows Forms app for capturing LinkedIn job-card pastes. Persistence is shared via an Azure-hosted API; the API keeps a **SQLite** database on Azure Files.

## What it does

1. Paste a LinkedIn job card (format varies; parser is best-effort).
2. Extract **Job Title**, **Company**, **Location**, **Viewed**, **Posted** text/date, **Top applicant**, and compensation text when present.
3. Build a deterministic **JobKey** = letters/numbers only from `Title + Company + Location` (primary key; upserts avoid duplicates).
4. Manually attach an **Apply URL** (LinkedIn paste does not include it reliably).
5. Record **Applied at**, **Comp specified**, and **Resume used**.

## Shared SQLite on Azure (done)

| Piece | Value |
|---|---|
| Storage account | `cjmpipelinefiles` |
| File share | `pipeline-data` |
| DB file | `cjm-apply.db` |
| Path inside Container Apps | `/mnt/pipeline/cjm-apply.db` |
| Existing mount | `pipeline-mcp` already mounts share as `pipelinefiles` → `/mnt/pipeline` |

Schema: `Sql/CreateSqlite.sql`. Local template copy: `data/cjm-apply.db`.

**Important:** SQLite on a file share needs a **single writer replica** (`maxReplicas: 1`) for the API container.

## SQL relay API (`CjmApplyApi`)

Minimal ASP.NET API that executes whatever SQL you send against the shared SQLite file.

| | |
|---|---|
| `GET /health` | Liveness + DB path |
| `POST /sql` | Body: `{ "sql": "...", "parameters": { "name": "value" } }` |
| Auth | Header `X-Api-Key` or `Authorization: Bearer …` (`CJM_APPLY_API_KEY`) |

- `SELECT` / `WITH` / `PRAGMA` / `EXPLAIN` → `{ columns, rows, rowCount }`
- Other statements (`INSERT`/`UPDATE`/`DELETE`/DDL) → `{ rowsAffected }`

Local run:

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
$env:CJM_APPLY_DB = "..\data\cjm-apply.db"
$env:CJM_APPLY_API_KEY = "dev-key"
dotnet run --no-launch-profile --urls http://127.0.0.1:5088
```

Deploy to Container Apps (Docker Desktop required; ACR Tasks blocked on this sub):

```powershell
cd C:\Users\chris\Projects\CjmApplyAssistant\CjmApplyApi
.\deploy.ps1 -ApiKey "pick-a-long-secret"
```

## Run desktop app

```bash
cd C:\Users\chris\Projects\CjmApplyAssistant
dotnet run
```

## Typical workflow

1. On LinkedIn, copy a job card from search results.
2. Paste into the app → **Parse Paste**.
3. Fix any mis-parsed fields (LI layouts vary; blank/misread fields are normal).
4. Use **LinkedIn Search** to find the posting, copy the job URL into **Apply URL**.
5. When you apply: check **Applied**, set **Comp specified** / **Resume used**, then save via the API.

To paste several cards at once, separate them with a line of `---` between cards.

## Schema

Single table `Jobs`. Primary key `JobKey` (alphanumeric concat of title, company, location).
