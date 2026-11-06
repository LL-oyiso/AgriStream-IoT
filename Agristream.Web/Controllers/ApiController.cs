using System.Diagnostics;
using System.Security.Cryptography;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

public record TelemetrySurgeRequest(string FieldId, int Sensors = 50, int ReadingsPerSensor = 20, bool AutoIrrigate = false, int MaxIrrigation = 5);
public record DroneBurstRequest(string FieldId, int Count = 20);
public record IrrigationApiRequest(string DeviceId, string FieldId, string Action = "start", int DurationMinutes = 30);

/// <summary>
/// JSON endpoints for the load-test script and the simulator. All of them require the
/// X-Simulator-Key header and are disabled (404) if the SimulatorKey setting is empty, except /api/ping.
/// </summary>
[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly SimulatorService _simulator;
    private readonly EvidenceService _evidence;
    private readonly IFunctionsClient _functions;

    public ApiController(SimulatorService simulator, EvidenceService evidence, IFunctionsClient functions)
    {
        _simulator = simulator;
        _evidence = evidence;
        _functions = functions;
    }

    /// <summary>Cheap anonymous health check that also reports which instance answered.</summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok", instance = ConfigStatus.InstanceId, utc = DateTimeOffset.UtcNow });

    [HttpPost("simulate/telemetry"), SimulatorKey]
    public async Task<IActionResult> SimulateTelemetry([FromBody] TelemetrySurgeRequest request, CancellationToken ct)
    {
        var result = await _simulator.RunTelemetrySurgeAsync(
            request.FieldId, request.Sensors, request.ReadingsPerSensor, request.AutoIrrigate, request.MaxIrrigation, "load-test", ct);
        return Ok(result);
    }

    [HttpPost("simulate/images"), SimulatorKey]
    public async Task<IActionResult> SimulateImages([FromBody] DroneBurstRequest request, CancellationToken ct) =>
        Ok(await _simulator.RunDroneBurstAsync(request.FieldId, request.Count, "load-test", ct));

    /// <summary>Queues one irrigation command through the POST function (exercises the whole queue path).</summary>
    [HttpPost("simulate/irrigation"), SimulatorKey]
    public async Task<IActionResult> SimulateIrrigation([FromBody] IrrigationApiRequest request, CancellationToken ct)
    {
        var result = await _functions.TriggerIrrigationAsync(
            new IrrigationRequest(request.DeviceId, request.FieldId, request.Action, request.DurationMinutes, "load-test", "Load test"), ct);
        return result.Success ? Accepted(result) : StatusCode(502, result);
    }

    /// <summary>Calls the GET function the same way the dashboard does.</summary>
    [HttpGet("telemetry/{fieldId}"), SimulatorKey]
    public async Task<IActionResult> Telemetry(string fieldId, CancellationToken ct)
    {
        var result = await _functions.GetTelemetryAsync(fieldId, 20, ct);
        return result.Data is null
            ? StatusCode(502, new { result.Error })
            : Ok(new { result.Data.Count, result.Data.Anomalies, functionMs = result.RoundTripMs, instance = ConfigStatus.InstanceId });
    }

    [HttpGet("evidence"), SimulatorKey]
    public async Task<IActionResult> Evidence(CancellationToken ct) => Ok(await _evidence.GetAsync(ct));

    /// <summary>Burns CPU for the requested milliseconds so the plan's CPU metric visibly rises under load.</summary>
    [HttpGet("load/work"), SimulatorKey]
    public IActionResult Work([FromQuery] int ms = 200)
    {
        ms = Math.Clamp(ms, 1, 2000);
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[1024];
        long rounds = 0;

        while (stopwatch.ElapsedMilliseconds < ms)
        {
            buffer = SHA256.HashData(buffer);
            rounds++;
        }

        return Ok(new { ms, rounds, instance = ConfigStatus.InstanceId });
    }
}
