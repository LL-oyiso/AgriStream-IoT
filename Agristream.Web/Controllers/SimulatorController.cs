using Agristream.Web.Models;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

/// <summary>Traffic simulator form (Admin only). The JSON equivalents live in ApiController.</summary>
[Authorize(Roles = Roles.Admin)]
public class SimulatorController : Controller
{
    private const string SurgeKey = "LastSurge";
    private const string BurstKey = "LastBurst";

    private readonly SimulatorService _simulator;
    private readonly IDeviceStore _devices;

    public SimulatorController(SimulatorService simulator, IDeviceStore devices)
    {
        _simulator = simulator;
        _devices = devices;
    }

    public async Task<IActionResult> Index()
    {
        var model = new SimulatorViewModel();
        try
        {
            model.Fields = await _devices.ListFieldsAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // Fall back to the default fields below.
        }

        if (model.Fields.Count == 0)
        {
            model.Fields = ["field-01", "field-02", "field-03"];
        }

        if (TempData[SurgeKey] is string surge)
            model.LastSurge = System.Text.Json.JsonSerializer.Deserialize<TelemetrySurgeResult>(surge);
        if (TempData[BurstKey] is string burst)
            model.LastBurst = System.Text.Json.JsonSerializer.Deserialize<DroneBurstResult>(burst);

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Telemetry(string fieldId, int sensors, int readingsPerSensor, bool autoIrrigate, int maxIrrigation)
    {
        try
        {
            var result = await _simulator.RunTelemetrySurgeAsync(
                fieldId, sensors, readingsPerSensor, autoIrrigate, maxIrrigation, User.Identity?.Name ?? "admin", HttpContext.RequestAborted);
            TempData[SurgeKey] = System.Text.Json.JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Telemetry surge failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Drones(string fieldId, int count)
    {
        try
        {
            var result = await _simulator.RunDroneBurstAsync(fieldId, count, User.Identity?.Name ?? "admin", HttpContext.RequestAborted);
            TempData[BurstKey] = System.Text.Json.JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Drone burst failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
