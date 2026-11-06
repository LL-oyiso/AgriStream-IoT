using Agristream.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Both services are resolved on first use rather than at startup, so the four functions still
// appear in the portal (with a clear error in the logs) if a setting is missing.
builder.Services.AddSingleton(_ =>
{
    var storage = builder.Configuration["AzureWebJobsStorage"];
    if (string.IsNullOrWhiteSpace(storage))
    {
        throw new InvalidOperationException(
            "AzureWebJobsStorage is not configured. Set it in local.settings.json (local) " +
            "or in the Function App's environment variables (Azure).");
    }

    return new TableStorageFactory(storage);
});

builder.Services.AddSingleton<IDeviceRegistry>(_ =>
{
    var sql = builder.Configuration["SqlConnectionString"];
    if (string.IsNullOrWhiteSpace(sql))
    {
        throw new InvalidOperationException(
            "SqlConnectionString is not configured. Set it in local.settings.json (local) " +
            "or in the Function App's environment variables (Azure).");
    }

    return new SqlDeviceRegistry(sql);
});

builder.Build().Run();
