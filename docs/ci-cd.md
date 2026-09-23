# CI/CD pipeline

This repository uses `.github/workflows/dotnetcore.yml` for CI/CD.

## Triggers

- `pull_request` (CI validation only)
- `push` to `main`
- `workflow_dispatch`

## Stage order

The jobs are chained with `needs` and run in this order:

1. **Source checkout** (`source`)
2. **Build** (`build`)
3. **Fast tests** (`fast-tests`)
4. **Slow tests** (`slow-tests`)
5. **Production deploy** (`production-deploy`, only on `main` push/manual runs)

## Build once, deploy everywhere

The build stage restores and compiles from `eShopOnWeb.sln`, then creates one deployable artifact from:

- `dotnet build ./eShopOnWeb.sln --configuration Release --no-restore -p:LibraryRestore=false`
- `dotnet publish ./src/Web/Web.csproj --configuration Release --no-build --output ./artifacts/web -p:LibraryRestore=false`

Artifact strategy:

- Artifact name: `webapp-release`
- Retention: `14` days
- Upload action: `actions/upload-artifact@v4`
- Deploy stage downloads the exact same artifact with `actions/download-artifact@v4` and does **not** rebuild before deploy.

## Fast vs slow tests

### Fast tests

Runs only unit tests:

- `dotnet test ./tests/UnitTests/UnitTests.csproj --configuration Release --no-restore --blame-hang --blame-hang-timeout 5m -p:LibraryRestore=false`

Guardrails:

- Job timeout: `15` minutes
- Hang detection enabled with `--blame-hang`

### Slow tests

Runs integration/API/functional test projects separately:

- `tests/IntegrationTests/IntegrationTests.csproj`
- `tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj`
- `tests/FunctionalTests/FunctionalTests.csproj`

Each uses `--blame-hang --blame-hang-timeout 10m` and the job has a `30` minute timeout.
Each command also sets `-p:LibraryRestore=false` to avoid CI-only failures caused by LibMan CDN resolution in non-interactive build agents.

## Production environment protection

The deploy job uses:

- `environment: production`

This allows repository admins to configure required reviewers/manual approval in:

- **Settings → Environments → production**

Deployment only runs for trusted `main` branch flows (`push`/`workflow_dispatch`), never for untrusted pull-request code.

## Deployment target and required secrets

The repository contains `azure.yaml` with `host: appservice`, so the workflow supports Azure Web App deployment when secrets are configured.

Required secrets (preferably on the `production` environment):

- `AZURE_WEBAPP_NAME`
- `AZURE_WEBAPP_PUBLISH_PROFILE`

If these are not configured, deploy is a safe placeholder:

- It still downloads and validates the artifact (`Web.dll` check),
- then writes clear setup guidance to workflow summary/notice.

No production credentials are stored in the artifact or repository.

## Failure notifications

The `notify-failure` job always runs and writes a concise stage-result summary to the GitHub Actions run summary.

- On failed/cancelled stages it emits a GitHub Actions `::error` annotation.
- This is credential-free and repository-compatible.

Optional integrations (Slack/email/Teams) can be added later through repository or environment secrets and an extra notification step.
