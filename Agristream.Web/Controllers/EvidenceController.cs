using Agristream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

/// <summary>
/// Evidence dashboard (Admin only): queue backlog, storage counts, throughput and latency. It
/// replaces Application Insights, which is not available on this subscription.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class EvidenceController : Controller
{
    private readonly EvidenceService _evidence;

    public EvidenceController(EvidenceService evidence)
    {
        _evidence = evidence;
    }

    public async Task<IActionResult> Index() => View(await _evidence.GetAsync(HttpContext.RequestAborted));
}
