using System.Net;
using System.Net.Http.Headers;
using System.Text;
using sblngavnav6.Data;

namespace sblngavnav6.Services;

public sealed record PgApiResult(bool Ok, HttpStatusCode? Status, string Body, string Error)
{
    public static PgApiResult Fail(string error) => new(false, null, null, error);

    public string ToDisplay()
    {
        if (!Ok && Status is null)
            return $"❌ {Error}";

        var sb = new StringBuilder();
        sb.AppendLine($"Status: {(int)Status.Value} {Status.Value}");
        sb.AppendLine("```json");
        sb.AppendLine(Body);
        sb.AppendLine("```");
        return sb.ToString();
    }
}

public sealed class PgApiService
{
    private const int MaxBodyLength = 1600;

    private readonly IHttpClientFactory _httpClientFactory;

    public PgApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<PgApiResult> HealthAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "/health", authRequired: false, cancellationToken);

    public Task<PgApiResult> ProjectsAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "/api/projects", cancellationToken: cancellationToken);

    public Task<PgApiResult> ProjectStatusAsync(string project, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, $"/api/projects/{Uri.EscapeDataString(project)}/status", cancellationToken: cancellationToken);

    public Task<PgApiResult> ServicesAsync(string project, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, $"/api/projects/{Uri.EscapeDataString(project)}/services", cancellationToken: cancellationToken);

    public Task<PgApiResult> LogsAsync(string project, string service = null, int tail = 100, CancellationToken cancellationToken = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var suffix = service is null
            ? $"/api/projects/{encodedProject}/logs?tail={tail}"
            : $"/api/projects/{encodedProject}/logs?service={Uri.EscapeDataString(service)}&tail={tail}";

        return SendAsync(HttpMethod.Get, suffix, cancellationToken: cancellationToken);
    }

    public Task<PgApiResult> RestartProjectAsync(string project, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/projects/{Uri.EscapeDataString(project)}/restart", cancellationToken: cancellationToken);

    public Task<PgApiResult> RestartServiceAsync(string project, string service, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/projects/{Uri.EscapeDataString(project)}/restart/{Uri.EscapeDataString(service)}", cancellationToken: cancellationToken);

    public Task<PgApiResult> StopProjectAsync(string project, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/projects/{Uri.EscapeDataString(project)}/stop", cancellationToken: cancellationToken);

    public Task<PgApiResult> StartProjectAsync(string project, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/projects/{Uri.EscapeDataString(project)}/start", cancellationToken: cancellationToken);

    private async Task<PgApiResult> SendAsync(
        HttpMethod method,
        string route,
        bool authRequired = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.pgApiBaseUrl))
            return PgApiResult.Fail("PG API URL не задан");

        if (!Uri.TryCreate(Global.Vars.Cfg.pgApiBaseUrl, UriKind.Absolute, out var baseUri))
            return PgApiResult.Fail($"PG API URL невалидный: {Global.Vars.Cfg.pgApiBaseUrl}");

        var client = _httpClientFactory.CreateClient(nameof(PgApiService));
        client.BaseAddress = baseUri;

        using var request = new HttpRequestMessage(method, route);

        if (authRequired)
        {
            if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.pgApiToken))
                return PgApiResult.Fail("Токен не задан или протух");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Global.Vars.Cfg.pgApiToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return PgApiResult.Fail($"Запрос не дошёл: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PgApiResult.Fail("Таймаут запроса");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(body))
                body = "(пустой ответ)";
            else if (body.Length > MaxBodyLength)
                body = body[..MaxBodyLength] + "\n... (обрезано)";

            return new PgApiResult(
                response.IsSuccessStatusCode,
                response.StatusCode,
                body,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
    }
}
