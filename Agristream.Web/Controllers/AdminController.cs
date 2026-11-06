using Agristream.Web.Models;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly IUserStore _users;

    public AdminController(IUserStore users)
    {
        _users = users;
    }

    public async Task<IActionResult> Users()
    {
        var model = new UsersViewModel();
        try
        {
            model.Users = await _users.ListAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            model.Error = $"User database unavailable: {ex.Message}";
        }

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(int userId, string role)
    {
        if (!Roles.All.Contains(role))
        {
            TempData["Error"] = "Unknown role.";
            return RedirectToAction(nameof(Users));
        }

        await _users.UpdateRoleAsync(userId, role, HttpContext.RequestAborted);
        TempData["Success"] = "Role updated.";
        return RedirectToAction(nameof(Users));
    }
}
