using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Agristream.Web.Services;

public record ReadingDto(
    string SensorId,
    double SoilMoisturePct,
    double SoilTempC,
    double Ph,
    double BatteryPct,
    bool IsAnomaly,
    DateTimeOffset ReadingUtc);

public record TelemetryResponse(
    string FieldId,
    int Count,
    int Anomalies,
    long ServerElapsedMs,
    DateTimeOffset GeneratedUtc,
    List<ReadingDto> Readings);

public record TelemetryResult(TelemetryResponse? Data, long RoundTripMs, string? Error);

public record IrrigationRequest(
    string DeviceId,
    string FieldId,
    string Action,
    int DurationMinutes,
    string RequestedBy,
    string? Reason);

public record IrrigationResult(bool Success, Guid? CommandId, string Message);

public interface IFunctionsClient
{
    Task<TelemetryResult> GetTelemetryAsync(string fieldId, int limit, CancellationToken ct = default);
    Task<IrrigationResult> TriggerIrrigationAsync(IrrigationRequest request, CancellationToken ct = default);
}

/// <summary>
/// Calls the HTTP-triggered functions. The base URL and function key come from app settings and
/// are read per call, so a missing setting becomes a friendly message rather than a crash.
/// </summary>
public class FunctionsClient : IFunctionsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<FunctionsClient> _logger;

    public FunctionsClient(HttpClient http, IConfiguration config, ILogger<FunctionsClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<TelemetryResult> GetTelemetryAsync(string fieldId, int limit, CancellationToken ct = default)
    {
        if (!TryBuildRequest(HttpMethod.Get, $"api/telemetry/{Uri.EscapeDataString(fieldId)}?limit={limit}", out var request, out var error))
        {
            return new TelemetryResult(null, 0, error);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(request!, ct);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new TelemetryResult(null, stopwatch.ElapsedMilliseconds,
                    $"The telemetry function returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var data = await response.Content.ReadFromJsonAsync<TelemetryResponse>(Json, ct);
            return new TelemetryResult(data, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Telemetry call failed.");
            return new TelemetryResult(null, stopwatch.ElapsedMilliseconds, "The telemetry function could not be reached.");
        }
    }

    public async Task<IrrigationResult> TriggerIrrigationAsync(IrrigationRequest request, CancellationToken ct = default)
    {
        if (!TryBuildRequest(HttpMethod.Post, "api/irrigation", out var message, out var error))
        {
            return new IrrigationResult(false, null, error!);
        }

        message!.Content = JsonContent.Create(request, options: Json);

        try
        {
            using var response = await _http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                Guid? id = body.TryGetProperty("commandId", out var value) && value.TryGetGuid(out var guid) ? guid : null;
                return new IrrigationResult(true, id, "Irrigation command queued.");
            }

            var detail = await response.Content.ReadAsStringAsync(ct);
            return new IrrigationResult(false, null, $"The irrigation function returned {(int)response.StatusCode}: {detail}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Irrigation call failed.");
            return new IrrigationResult(false, null, "The irrigation function could not be reached.");
        }
    }

    private bool TryBuildRequest(HttpMethod method, string relativePath, out HttpRequestMessage? request, out string? error)
    {
        var baseUrl = _config[SettingNames.FunctionBaseUrl];
        var key = _config[SettingNames.FunctionKey];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
        {
            request = null;
            error = $"{SettingNames.FunctionBaseUrl} and {SettingNames.FunctionKey} must be configured.";
            return false;
        }

        request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), relativePath));
        request.Headers.Add("x-functions-key", key);
        error = null;
        return true;
    }
}
