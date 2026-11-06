using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Agristream.Web.Services;

/// <summary>
/// Protects the JSON simulator and load-test endpoints with the X-Simulator-Key header. If the
/// SimulatorKey app setting is not set, the endpoints are disabled (404), so they are never open.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SimulatorKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[SettingNames.SimulatorKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var supplied = context.HttpContext.Request.Headers["X-Simulator-Key"].ToString();
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        if (!CryptographicOperations.FixedTimeEquals(a, b))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
