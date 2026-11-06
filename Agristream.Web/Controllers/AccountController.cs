using System.Security.Claims;
using Agristream.Web.Models;
using Agristream.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agristream.Web.Controllers;

public class AccountController : Controller
{
    private readonly IUserStore _users;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IUserStore users, ILogger<AccountController> logger)
    {
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrEmpty(model.Password))
        {
            ModelState.AddModelError(string.Empty, "Enter your username and password.");
            return View(model);
        }

        UserAccount? user;
        try
        {
            user = await _users.FindByUsernameAsync(model.Username.Trim(), HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed: database unavailable.");
            ModelState.AddModelError(string.Empty, "The user database is not reachable. Check the SqlConnectionString setting.");
            return View(model);
        }

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
            ? Redirect(model.ReturnUrl)
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        var username = model.Username?.Trim() ?? string.Empty;

        if (username.Length is < 3 or > 50)
            ModelState.AddModelError(nameof(model.Username), "Username must be 3 to 50 characters.");
        if ((model.Password ?? string.Empty).Length < 8)
            ModelState.AddModelError(nameof(model.Password), "Password must be at least 8 characters.");
        if (model.Password != model.ConfirmPassword)
            ModelState.AddModelError(nameof(model.ConfirmPassword), "The passwords do not match.");
        // Admin accounts cannot be self-registered: an Admin assigns that role.
        if (!Roles.SelfRegister.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Choose Farmer or Agronomist.");

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var created = await _users.CreateAsync(username, PasswordHasher.Hash(model.Password!), model.Role, HttpContext.RequestAborted);
            if (!created)
            {
                ModelState.AddModelError(nameof(model.Username), "That username is already taken.");
                return View(model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed: database unavailable.");
            ModelState.AddModelError(string.Empty, "The user database is not reachable. Check the SqlConnectionString setting.");
            return View(model);
        }

        // Post/Redirect/Get: a browser reload cannot register the user twice.
        TempData["Success"] = $"Account '{username}' created as {model.Role}. You can sign in now.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();
}
