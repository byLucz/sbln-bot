using System.Net.Http.Headers;
using System.Text;
using sblngavnav5X.Data;

namespace sblngavnav5X.Services;

public sealed class PgApiService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PgApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> HealthAsync()
        => await SendAsync(HttpMethod.Get, "/health", authRequired: false);

    public async Task<string> ProjectsAsync()
        => await SendAsync(HttpMethod.Get, "/api/projects");

    public async Task<string> ProjectStatusAsync(string project)
        => await SendAsync(HttpMethod.Get, $"/api/projects/{project}/status");

    public async Task<string> ServicesAsync(string project)
        => await SendAsync(HttpMethod.Get, $"/api/projects/{project}/services");

    public async Task<string> LogsAsync(string project, string service = null, int tail = 100)
    {
        var suffix = service is null
            ? $"/api/projects/{project}/logs?tail={tail}"
            : $"/api/projects/{project}/logs?service={Uri.EscapeDataString(service)}&tail={tail}";

        return await SendAsync(HttpMethod.Get, suffix);
    }

    public async Task<string> RestartProjectAsync(string project)
        => await SendAsync(HttpMethod.Post, $"/api/projects/{project}/restart");

    public async Task<string> RestartServiceAsync(string project, string service)
        => await SendAsync(HttpMethod.Post, $"/api/projects/{project}/restart/{Uri.EscapeDataString(service)}");

    public async Task<string> StopProjectAsync(string project)
        => await SendAsync(HttpMethod.Post, $"/api/projects/{project}/stop");

    public async Task<string> StartProjectAsync(string project)
        => await SendAsync(HttpMethod.Post, $"/api/projects/{project}/start");

    private async Task<string> SendAsync(HttpMethod method, string route, bool authRequired = true)
    {
        if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.pgApiBaseUrl))
            return "❌ PG API URL не задан";

        if (!Uri.TryCreate(Global.Vars.Cfg.pgApiBaseUrl, UriKind.Absolute, out var baseUri))
            return $"❌ PG API URL невалидный: {Global.Vars.Cfg.pgApiBaseUrl}";

        var client = _httpClientFactory.CreateClient(nameof(PgApiService));
        client.BaseAddress = baseUri;

        using var request = new HttpRequestMessage(method, route);

        if (authRequired)
        {
            if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.pgApiToken))
                return "❌ Токен не задан или протух";

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Global.Vars.Cfg.pgApiToken);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(body))
            body = "(пустой ответ)";

        var normalized = body.Length > 1600 ? body[..1600] + "\n... (обрезано)" : body;
        var sb = new StringBuilder();
        sb.AppendLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        sb.AppendLine("```json");
        sb.AppendLine(normalized);
        sb.AppendLine("```");
        return sb.ToString();
    }
}
