using Agristream.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Stateless by design: sign-in state lives in an encrypted cookie (the data-protection keys are
// stored in the App Service's shared %HOME% folder, so every instance can read it), uploads go
// straight to Blob storage, and no session state or local files are used. This is what lets the
// plan scale out to several instances.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 30 * 1024 * 1024);

// Every connection string is read from app settings when first used, so the site still starts
// (and shows a clear warning) if a setting is missing in Azure.
builder.Services.AddSingleton<ConfigStatus>();
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<StorageClients>();
builder.Services.AddSingleton<IUserStore, SqlUserStore>();
builder.Services.AddSingleton<IDeviceStore, SqlDeviceStore>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddSingleton<SimulatorService>();
builder.Services.AddSingleton<EvidenceService>();
builder.Services.AddHttpClient<IFunctionsClient, FunctionsClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService<AdminSeeder>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Tags every response with the instance that served it, so the load test can show requests
// being spread across instances after scale-out.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Instance-Id"] = ConfigStatus.InstanceId;
        return Task.CompletedTask;
    });
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
