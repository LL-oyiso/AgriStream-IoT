-- =====================================================================
-- Agristream IoT - Azure SQL scripts for the portal Query editor
-- (SQL database -> Query editor). No GO statements, so each PART can be
-- pasted and run as one batch. Run PART 1 once. Run PART 2 queries one
-- at a time, whenever you need evidence.
-- =====================================================================


-- =====================================================================
-- PART 1: SCHEMA (run once)
-- =====================================================================

-- Users: login credentials and assigned system role.
-- PasswordHash holds a salted PBKDF2 hash, never the plain-text password.
CREATE TABLE dbo.Users (
    UserId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Username      NVARCHAR(50)  NOT NULL,
    PasswordHash  NVARCHAR(200) NOT NULL,
    Role          NVARCHAR(20)  NOT NULL,
    CreatedUtc    DATETIME2(0)  NOT NULL CONSTRAINT DF_Users_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT CK_Users_Role CHECK (Role IN ('Farmer', 'Agronomist', 'Admin'))
);

-- Devices: the master registry of field devices. The queue-triggered
-- function ProcessIrrigationCommand updates Status and the Last* columns.
CREATE TABLE dbo.Devices (
    DeviceId        NVARCHAR(40)  NOT NULL CONSTRAINT PK_Devices PRIMARY KEY,
    FieldId         NVARCHAR(40)  NOT NULL,
    DeviceType      NVARCHAR(20)  NOT NULL,
    Name            NVARCHAR(80)  NOT NULL,
    Status          NVARCHAR(20)  NOT NULL CONSTRAINT DF_Devices_Status DEFAULT 'Idle',
    LastCommandId   UNIQUEIDENTIFIER NULL,
    LastCommandUtc  DATETIME2(3)  NULL,
    LastUpdatedUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_Devices_LastUpdatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Devices_Status CHECK (Status IN ('Idle', 'Irrigating', 'Offline', 'Fault'))
);

CREATE INDEX IX_Devices_FieldId ON dbo.Devices (FieldId, DeviceType);

-- IrrigationCommandLog: one row per processed queue message. CommandId is the
-- primary key, so a redelivered queue message (at-least-once delivery) is
-- detected and ignored instead of being applied twice.
CREATE TABLE dbo.IrrigationCommandLog (
    CommandId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_IrrigationCommandLog PRIMARY KEY,
    DeviceId         NVARCHAR(40)  NOT NULL,
    FieldId          NVARCHAR(40)  NOT NULL,
    Action           NVARCHAR(10)  NOT NULL,
    DurationMinutes  INT           NOT NULL,
    RequestedBy      NVARCHAR(50)  NOT NULL,
    Reason           NVARCHAR(200) NULL,
    EnqueuedUtc      DATETIME2(3)  NOT NULL,
    ProcessedUtc     DATETIME2(3)  NOT NULL,
    Outcome          NVARCHAR(20)  NOT NULL,
    LatencyMs        INT           NOT NULL
);

CREATE INDEX IX_IrrigationCommandLog_ProcessedUtc ON dbo.IrrigationCommandLog (ProcessedUtc DESC);
CREATE INDEX IX_IrrigationCommandLog_DeviceId ON dbo.IrrigationCommandLog (DeviceId, ProcessedUtc DESC);

-- Fabricated device registry: 3 fields, each with 2 valves, 1 pump and 1 weather station.
INSERT INTO dbo.Devices (DeviceId, FieldId, DeviceType, Name) VALUES
('V-F01-01', 'field-01', 'Valve',   'Field 01 North valve'),
('V-F01-02', 'field-01', 'Valve',   'Field 01 South valve'),
('P-F01-01', 'field-01', 'Pump',    'Field 01 pump'),
('W-F01-01', 'field-01', 'Weather', 'Field 01 weather station'),
('V-F02-01', 'field-02', 'Valve',   'Field 02 North valve'),
('V-F02-02', 'field-02', 'Valve',   'Field 02 South valve'),
('P-F02-01', 'field-02', 'Pump',    'Field 02 pump'),
('W-F02-01', 'field-02', 'Weather', 'Field 02 weather station'),
('V-F03-01', 'field-03', 'Valve',   'Field 03 North valve'),
('V-F03-02', 'field-03', 'Valve',   'Field 03 South valve'),
('P-F03-01', 'field-03', 'Pump',    'Field 03 pump'),
('W-F03-01', 'field-03', 'Weather', 'Field 03 weather station');

-- The admin user is created by the web app on first start (see README):
-- set the SeedAdminPassword app setting and the app inserts the 'admin' user
-- with a salted hash. Farmer and Agronomist users are created on the Register page.


-- =====================================================================
-- PART 2: EVIDENCE QUERIES (run one at a time)
-- =====================================================================

-- A. Users and their assigned roles (hash shortened, never show the full value)
SELECT UserId, Username, LEFT(PasswordHash, 12) + '...' AS PasswordHashStart, Role, CreatedUtc
FROM dbo.Users
ORDER BY UserId;

-- B. Users per role
SELECT Role, COUNT(*) AS Users
FROM dbo.Users
GROUP BY Role
ORDER BY Role;

-- C. Device registry with current operational status
SELECT DeviceId, FieldId, DeviceType, Status, LastCommandId, LastCommandUtc, LastUpdatedUtc
FROM dbo.Devices
ORDER BY FieldId, DeviceId;

-- D. Devices per status
SELECT Status, COUNT(*) AS Devices
FROM dbo.Devices
GROUP BY Status;

-- E. Most recent processed irrigation commands (queue message -> SQL)
SELECT TOP 20 CommandId, DeviceId, Action, DurationMinutes, RequestedBy, Outcome, EnqueuedUtc, ProcessedUtc, LatencyMs
FROM dbo.IrrigationCommandLog
ORDER BY ProcessedUtc DESC;

-- F. Totals: commands processed, average and maximum queue latency
SELECT COUNT(*) AS CommandsProcessed,
       AVG(CAST(LatencyMs AS FLOAT)) AS AvgLatencyMs,
       MAX(LatencyMs) AS MaxLatencyMs,
       MIN(ProcessedUtc) AS FirstProcessedUtc,
       MAX(ProcessedUtc) AS LastProcessedUtc
FROM dbo.IrrigationCommandLog;

-- G. Throughput per minute (commands processed) with latency
SELECT DATEADD(MINUTE, DATEDIFF(MINUTE, 0, ProcessedUtc), 0) AS MinuteUtc,
       COUNT(*) AS CommandsProcessed,
       AVG(CAST(LatencyMs AS FLOAT)) AS AvgLatencyMs,
       MAX(LatencyMs) AS MaxLatencyMs
FROM dbo.IrrigationCommandLog
GROUP BY DATEADD(MINUTE, DATEDIFF(MINUTE, 0, ProcessedUtc), 0)
ORDER BY MinuteUtc DESC;

-- H. Outcomes (Applied, UnknownDevice)
SELECT Outcome, COUNT(*) AS Commands
FROM dbo.IrrigationCommandLog
GROUP BY Outcome;

-- I. Anomalies: commands that waited longer than 5 seconds in the queue
SELECT TOP 20 CommandId, DeviceId, EnqueuedUtc, ProcessedUtc, LatencyMs
FROM dbo.IrrigationCommandLog
WHERE LatencyMs > 5000
ORDER BY LatencyMs DESC;

-- J. Commands per device (which valves are used most)
SELECT DeviceId, COUNT(*) AS Commands, MAX(ProcessedUtc) AS LastProcessedUtc
FROM dbo.IrrigationCommandLog
GROUP BY DeviceId
ORDER BY Commands DESC;

-- K. Empty the log between load tests (optional)
-- DELETE FROM dbo.IrrigationCommandLog;
-- UPDATE dbo.Devices SET Status = 'Idle', LastCommandId = NULL, LastCommandUtc = NULL;
