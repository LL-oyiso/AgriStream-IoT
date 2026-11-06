using Agristream.Web.Models;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

[Authorize]
public class ImagesController : Controller
{
    private readonly ImageService _images;
    private readonly IDeviceStore _devices;

    public ImagesController(ImageService images, IDeviceStore devices)
    {
        _images = images;
        _devices = devices;
    }

    public async Task<IActionResult> Index()
    {
        var model = new ImagesViewModel();

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

        try
        {
            model.Images = await _images.ListRecentAsync(24, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            model.Error = $"Blob storage unavailable: {ex.Message}";
        }

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, string fieldId)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose an image to upload.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var name = await _images.UploadAsync(file, fieldId, User.Identity?.Name ?? "unknown", HttpContext.RequestAborted);
            TempData["Success"] = $"Uploaded {name}. The blob-triggered function will write its audit entry in a few seconds.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Upload failed: {ex.Message}";
        }

        // Post/Redirect/Get: reloading cannot upload the same image twice.
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Streams a private blob to authorised users (the container itself stays private).</summary>
    public async Task<IActionResult> Photo(string name)
    {
        var download = await _images.OpenAsync(name, HttpContext.RequestAborted);
        return download is null
            ? NotFound()
            : File(download.Content, download.Details.ContentType ?? "application/octet-stream");
    }
}
