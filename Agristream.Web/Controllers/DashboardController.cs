using Agristream.Web.Models;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IFunctionsClient _functions;
    private readonly IDeviceStore _devices;

    public DashboardController(IFunctionsClient functions, IDeviceStore devices)
    {
        _functions = functions;
        _devices = devices;
    }

    /// <summary>Field dashboard: latest telemetry comes from the HTTP GET function, device status from Azure SQL.</summary>
    public async Task<IActionResult> Index(string fieldId = "field-01")
    {
        var model = new DashboardViewModel
        {
            FieldId = fieldId,
            InstanceId = ConfigStatus.InstanceId,
            CanTriggerIrrigation = User.IsInRole(Roles.Farmer) || User.IsInRole(Roles.Admin)
        };

        try
        {
            model.Fields = await _devices.ListFieldsAsync(HttpContext.RequestAborted);
            model.Devices = await _devices.ListAsync(fieldId, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            model.DeviceError = $"Device registry unavailable: {ex.Message}";
        }

        if (model.Fields.Count == 0)
        {
            model.Fields = ["field-01", "field-02", "field-03"];
        }

        model.Telemetry = await _functions.GetTelemetryAsync(fieldId, 20, HttpContext.RequestAborted);
        return View(model);
    }

    /// <summary>"Trigger irrigation": calls the HTTP POST function, which queues the command.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Farmer,Admin")]
    public async Task<IActionResult> TriggerIrrigation(string deviceId, string fieldId, string action, int durationMinutes = 30)
    {
        var result = await _functions.TriggerIrrigationAsync(
            new IrrigationRequest(deviceId, fieldId, action, durationMinutes, User.Identity?.Name ?? "unknown", "Manual request from dashboard"),
            HttpContext.RequestAborted);

        if (result.Success)
        {
            TempData["Success"] = $"{result.Message} Command {result.CommandId}. Refresh in a few seconds to see the device status change.";
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        // Post/Redirect/Get: reloading the page cannot queue the command twice.
        return RedirectToAction(nameof(Index), new { fieldId });
    }
}
