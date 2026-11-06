# Agristream IoT: Real-Time Processing System

| | |
|---|---|
| **Module** | CLDV7112 |
| **Practicum** | Building a real-time processing system (Agristream IoT) |
| **Student number** | `<STUDENT_NUMBER>` (placeholder, replace before submitting) |
| **Live URL** | https://st10504517agistream-gfdmh0avf2cpcdax.southafricanorth-01.azurewebsites.net |
| **Repository** | `<REPOSITORY_LINK>` (placeholder) |

Agristream IoT monitors large commercial farms. Soil sensors send telemetry in surges, and drones upload
high-resolution aerial images in bursts. This solution receives, stores and acts on those streams
without losing data, using an ASP.NET Core MVC web app and four Azure Functions.

## Architecture

```
Browser --> MVC Web App (App Service, B1) --login/roles--> Azure SQL (Users, Devices, IrrigationCommandLog)
                |   |
                |   +-- upload image --> Blob: drone-images --BlobTrigger--> ProcessDroneImage --> Table: ImageAudit
                |   +-- simulator ------> Table: SensorReadings
                |
                +-- GET  /api/telemetry/{fieldId} --> GetLatestTelemetry --> reads Table: SensorReadings
                +-- POST /api/irrigation ----------> QueueIrrigationCommand --> Queue: irrigation-commands
                                                          Queue --QueueTrigger--> ProcessIrrigationCommand
                                                                     --> Azure SQL: Devices (status), IrrigationCommandLog
                                                                     --> Table: IrrigationEvents
```

The Web App and the Function App share one **Basic B1** App Service plan in South Africa North.

## The four functions (exactly four)

| Function | Trigger | What it does |
|---|---|---|
| `ProcessDroneImage` | Blob trigger (`drone-images`) | Logs the upload and writes a processing-status audit row to Table storage |
| `GetLatestTelemetry` | HTTP GET `telemetry/{fieldId}` | Returns the latest readings from Table storage for the dashboard |
| `QueueIrrigationCommand` | HTTP POST `irrigation` | Validates the request and puts an irrigation command on the storage queue (queue output binding) |
| `ProcessIrrigationCommand` | Queue trigger (`irrigation-commands`) | Updates the device status in the Azure SQL master registry and logs the event |

## Features

- MVC login with **Farmer**, **Agronomist** and **Admin** roles (salted PBKDF2 hashes in Azure SQL).
- Field dashboard with latest telemetry, anomaly highlighting and a **Trigger irrigation** button (Post/Redirect/Get).
- Drone image upload to Blob storage, with the processing status written by the blob-triggered function.
- Admin pages: user roles, traffic **Simulator** (sensor surge and drone burst) and the **Evidence** dashboard
  (queue backlog, storage counts, throughput and latency, instance id).
- `scripts/load-test.ps1`: concurrent users, bursts, queue drain time, per-instance request counts.
- SQL scripts for the portal Query editor (`sql/create-tables.sql`: PART 1 schema, PART 2 evidence queries).

## Subscription constraints

The student subscription only has `Microsoft.Web`, `Microsoft.Storage` and `Microsoft.Sql` registered, and a
policy limits App Service plans to Basic or Shared. So there is no Application Insights, Azure Monitor autoscale
or Azure Load Testing. The substitutes are the in-app Evidence dashboard, portal Metrics, SQL DTU charts, a
PowerShell load-test script, and manual scale-out from 1 to 3 instances (autoscale rules are explained in the
submission document).

## Setup

1. Create the Azure resources with `docs/Practicum_Step_By_Step_Guide.docx` (portal steps).
2. Run **PART 1** of `sql/create-tables.sql` in the SQL database's Query editor.
3. Web App settings (Environment variables): `SqlConnectionString`, `StorageConnectionString`,
   `FunctionBaseUrl` (for example `https://<function-app>.azurewebsites.net`), `FunctionKey`
   (a function key from the Function App), `SimulatorKey` (a long random value) and `SeedAdminPassword`
   (creates the `admin` user on first start).
4. Function App settings: `SqlConnectionString` (`AzureWebJobsStorage` already exists).
5. Publish both projects from Visual Studio (Build, Publish, Azure).
6. Register a Farmer and an Agronomist on the Register page, then sign in as `admin`.

Never commit real connection strings, keys or passwords. Local copies go in `local.settings.json` and
`appsettings.Development.json`, which are git-ignored (see the `.example` files).
