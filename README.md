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

Quality gates, CI, and the Azure Static Web Apps deploy are in [Engineering practices](#engineering-practices).

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

## Engineering practices

The Angular app in `web/` is the quality-gated product. Tooling uses **npm** (the lockfile is `web/package-lock.json`) and **Node 22** in CI (`engines.node` allows 20.11+). Unit tests stay on the Karma/Jasmine setup the Angular 19 CLI already generated, running headless Chrome in CI, instead of replacing it with Jest. End-to-end tests are Playwright against the production build.

### What runs on a pull request

```text
pull request (and push to main)
├── CI / lint          ESLint (angular-eslint) and Prettier
├── CI / build         production build; artifact is the e2e input
├── CI / unit-tests    Karma, Chrome headless, coverage artifact + PR comment
├── CI / e2e           Playwright smoke tests against that production build
├── CodeQL / Analyze (javascript-typescript)
└── Azure Static Web Apps   deploy or preview; skips successfully if the token secret is absent
```

Any failure in `lint`, `build`, `unit-tests`, `e2e`, or CodeQL fails the pull request. The Azure job does not fail when `AZURE_STATIC_WEB_APPS_API_TOKEN` is missing: it logs a notice and skips checkout, build, and deploy.

### Run it locally

From `web/` (install once with `npm install`; that also installs the git hook):

| Command | What it does |
|---|---|
| `npm start` | Dev server at http://localhost:4200 |
| `npm run lint` | `ng lint` (TypeScript, templates, e2e) |
| `npm run format` | Prettier write |
| `npm run format:check` | Prettier check, same as CI |
| `npm test` | Karma in Chrome, watch mode |
| `npm run test:ci` | Headless Chrome, single run, coverage, thresholds |
| `npm run build -- --configuration=production` | Production bundle in `dist/jobgrid/browser` |
| `npm run e2e:install` | Download Playwright's Chromium (once per machine) |
| `npm run e2e` | Build must already exist; serves it and runs `e2e/` |

Pre-commit (Husky + lint-staged) runs ESLint `--fix` and Prettier on staged files under `web/`. The hook is installed by the `prepare` script. Skip it once with `HUSKY=0 git commit` only when you are intentionally committing a broken intermediate state.

### Coverage

`npm run test:ci` prints a summary and writes `web/coverage/jobgrid/` (`coverage-summary.json`, `lcov.info`, HTML). CI uploads that directory as the `coverage-report` artifact and, on pull requests, comments the totals (the comment is updated in place).

Starter specs cover the shell, routes, `ApiService`, paste/parse (including CSV escaping and API errors), the import-result modal, and the Job Grid stub. Measured baseline:

| Metric | Coverage | Gate in `web/karma.conf.js` |
|---|---|---|
| Statements | 99.18% (121/122) | 90% |
| Branches | 90.32% (56/62) | 80% |
| Functions | 100% (44/44) | 95% |
| Lines | 98.98% (98/99) | 90% |

The gates sit under the baseline so one awkward branch does not flake CI, and a deleted test still fails the build. The missed line is the last-resort fallback in `PasteParseComponent.errorMessage`. `main.ts` is not in this denominator: Karma starts from the specs. The Playwright smoke tests boot that file.

To raise a gate: add a real test, run `npm run test:ci`, then increase the matching number under `coverageReporter.check.global`. Do not exclude files to move the percentage.

### Branch protection for `main`

Do this in GitHub after the checks on this repository have run once (they only show up in the picker after that). These instructions are the settings to turn on; nothing in this repo changes them automatically.

**Settings → Branches → Add branch protection rule** (or edit the rule for `main`):

| Setting | Value |
|---|---|
| Branch name pattern | `main` |
| Require a pull request before merging | On |
| Required approvals | **1** |
| Require conversation resolution before merging | On |
| Require status checks to pass before merging | On |
| Require branches to be up to date before merging | On |
| Require these status checks (exact job names) | `lint`, `build`, `unit-tests`, `e2e`, `Analyze (javascript-typescript)` |
| Allow force pushes | **Off** |

Leave "Do not allow bypassing the above settings" on if you want admins held to the same rule. Also leave deletions off. The same requirements exist as a ruleset (Settings → Rules → Rulesets) if you prefer that UI; the check names do not change.

On a pull request the rows are grouped as `CI / lint`, `CI / build`, `CI / unit-tests`, `CI / e2e`, and `CodeQL / Analyze (javascript-typescript)`. The string branch protection stores is the job `name`, which is the list above. Search for that name in the status-check box after the workflows have completed once, and pick the GitHub Actions entry.

Do **not** require `azure-static-web-apps` until a deploy with the token has succeeded. It is not a test gate. While the secret is unset the job is green because it skips, which would hide a later token or Azure failure if you required it too early.

### Azure Static Web Apps

The SPA is a static Angular build. `web/public/staticwebapp.config.json` is copied into the production output and sends unknown routes (such as `/jobs`) back to `index.html`.

`Azure Static Web Apps` (`.github/workflows/azure-static-web-apps.yml`):

- Push to `main` deploys production.
- Pull requests opened, synchronized, or reopened deploy a preview environment.
- Closing the pull request closes that preview.
- If `AZURE_STATIC_WEB_APPS_API_TOKEN` is empty, the job emits a notice and skips deploy and preview close. It does not fail.

The API (`CjmApplyApi`) does **not** belong on Static Web Apps. It is an ASP.NET process with a SQLite file on Azure Files and a single writer (`maxReplicas: 1`). Keep deploying it with `CjmApplyApi/deploy.ps1` to **Azure Container Apps**. App Service can host that API if you would rather not run Container Apps, but you would still need a single-writer disk for SQLite; the file share mounted into Container Apps matches the current design, and Static Web Apps managed functions cannot.

The production bundle currently calls `apiBaseUrl: ''` (`web/src/environments/environment.production.ts`), so a deployed SPA will not reach the API until that origin is set. Do not put `CJM_APPLY_API_KEY` in the browser bundle. Add the Static Web App origin (and preview origins, if browsers call the API from previews) to the API `Cors:Origins` list. Production CORS ships empty on purpose.

### Dependency and security automation

- Dependabot (`.github/dependabot.yml`) opens weekly updates for `web/` npm packages and for GitHub Actions.
- CodeQL (`.github/workflows/codeql.yml`) analyzes JavaScript and TypeScript on pull requests, pushes to `main`, and a weekly schedule. C# is not in this workflow; the API would need a compiled `csharp` build mode if you want it scanned the same way.
- `CODEOWNERS` requests `@cmitroka` for review. Pull requests start from `.github/pull_request_template.md`.

### What you still do by hand

1. **Create the Azure Static Web App** (custom / other deployment source, not the Azure-generated workflow). Copy its deployment token.
2. **Add the Actions secret** `AZURE_STATIC_WEB_APPS_API_TOKEN` (Settings → Secrets and variables → Actions). The next push to `main` deploys; pull requests get previews.
3. **Point the SPA at the API** by setting `apiBaseUrl` in `environment.production.ts`, and allow that site in the API CORS list. Rebuild and deploy.
4. **Turn on branch protection** for `main` using the table above. Wait until the check names exist, then add them exactly.
5. **Enable secret scanning** (Settings → Code security and analysis → Secret scanning, plus push protection). On a private personal repository this needs GitHub Secret Protection; if the toggle is missing, that is the plan gap, not a missing file in this repo.
6. If the CodeQL job reports that code scanning is disabled, enable **GitHub Code Security** for this private repository. Leave the workflow in place (advanced setup). Do not also enable CodeQL default setup, or the two configurations will fight.
