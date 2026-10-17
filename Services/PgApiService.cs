using sblngavnav6.Common;
using sblngavnav6.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

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
        sb.AppendLine(Body?.Replace("```", "`‌``", StringComparison.Ordinal));
        sb.AppendLine("```");
        return sb.ToString();
    }
}

public sealed class PgApiService
{
    private const int MaxBodyLength = 1600;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

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
        if (authRequired && string.IsNullOrWhiteSpace(Global.Vars.Cfg.pgApiToken))
            return PgApiResult.Fail("Токен не задан или протух");

        var response = await CommonUtils.Web.SendAsync(
            _httpClientFactory, "PG API", Global.Vars.Cfg.pgApiBaseUrl, method, route, Timeout,
            request =>
            {
                if (authRequired)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Global.Vars.Cfg.pgApiToken);
            },
            cancellationToken);

        if (response.Status is null)
            return PgApiResult.Fail(response.Error);

        return new PgApiResult(
            response.Ok,
            response.Status,
            string.IsNullOrWhiteSpace(response.Body) ? "(пустой ответ)" : CommonUtils.Text.Truncate(response.Body, MaxBodyLength),
            response.Error);
    }
}
