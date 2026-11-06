# CONTEXT: Agristream IoT practicum (handover)

## Status
- Code written: Functions (4), Web (MVC), SQL scripts, load-test script.
- Docs written: see `docs/` (guide, submission, theory) once generated.
- Student number, live URL and repo link are still placeholders.

## Constraints (Rosebank student subscription)
- Providers registered: `Microsoft.Web`, `Microsoft.Storage`, `Microsoft.Sql` only.
- No Event Hubs, Cosmos DB, Application Insights, rule-based autoscale or Azure Load Testing.
- App Service plans must be Basic or Shared. One B1 plan is shared by the Web App and the Function App.
- Institution-owned resource group, South Africa North. Never create or delete resource groups.

## Decisions
- Exactly 4 functions (brief). Telemetry is ingested by the web app simulator writing batches straight into Table storage.
- Table keys: `SensorReadings` PartitionKey `{fieldId}_{yyyyMMdd}`, RowKey reverse ticks + sensor + sequence.
- HTTP functions use function-key auth; the key lives in the web app setting `FunctionKey`.
- POST function uses a queue output binding (Base64 encoding handled by the Functions host).
- Queue processing is idempotent: `IrrigationCommandLog.CommandId` is the primary key.
- Passwords: salted PBKDF2 in `dbo.Users.PasswordHash`. Admin is seeded from `SeedAdminPassword`.
- Simulator and load endpoints need the `X-Simulator-Key` header and are disabled when `SimulatorKey` is empty.
- `host.json` baseline is the default 16/8 queue batch settings. The optimisation commit lowers it to 6/3 so that
  functions x (batchSize + newBatchThreshold) x instances stays under the SQL Basic 30-request limit.

## Environment quirks
- OneDrive path is long: each project has `Directory.Build.props` redirecting `bin` and `obj` to `%LOCALAPPDATA%\Agristream_Build\`.
- Do not run `dotnet build` or `dotnet restore` in the real folders from the Cursor shell; verify in a `%TEMP%` copy.
- Function App needs Always On and 64-bit platform, otherwise queue and blob triggers stop or the host crashes.

## Pending
- Create Azure resources, publish, capture screenshots, fill placeholders in the submission document.
- Run baseline and optimised load tests and record the measured figures.
- Rewrite the theory answers in your own words.
