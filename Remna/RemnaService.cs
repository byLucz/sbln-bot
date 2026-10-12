using sblngavnav6.Common;
using sblngavnav6.Data;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace sblngavnav6.Remna;

public sealed record RemnaResult<T>(T Data, string Error)
{
    public bool Ok => Error is null;

    public static RemnaResult<T> Fail(string error) => new(default, error);
}

public sealed class RemnaService
{
    private const string RestartBody = "{\"forceRestart\":false}";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpClientFactory;

    public RemnaService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<RemnaResult<List<RemnaNode>>> NodesAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "/api/nodes", RemnaJsonContext.Default.RemnaEnvelopeListRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> NodeAsync(string uuid, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, $"/api/nodes/{Uri.EscapeDataString(uuid)}", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> EnableNodeAsync(string uuid, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/enable", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> DisableNodeAsync(string uuid, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/disable", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaEvent>> RestartNodeAsync(string uuid, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/restart", RemnaJsonContext.Default.RemnaEnvelopeRemnaEvent, RestartBody, cancellationToken);

    public Task<RemnaResult<RemnaEvent>> RestartAllAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, "/api/nodes/actions/restart-all", RemnaJsonContext.Default.RemnaEnvelopeRemnaEvent, RestartBody, cancellationToken);

    public Task<RemnaResult<RemnaStats>> StatsAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "/api/system/stats", RemnaJsonContext.Default.RemnaEnvelopeRemnaStats, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaHealth>> HealthAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "/api/system/health", RemnaJsonContext.Default.RemnaEnvelopeRemnaHealth, cancellationToken: cancellationToken);

    private async Task<RemnaResult<T>> SendAsync<T>(
        HttpMethod method,
        string route,
        JsonTypeInfo<RemnaEnvelope<T>> typeInfo,
        string json = null,
        CancellationToken cancellationToken = default)
    {
        if (!Global.Vars.Cfg.remnaEnabled)
            return RemnaResult<T>.Fail("Remnawave отключён в конфигурации");

        if (string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaToken))
            return RemnaResult<T>.Fail("Токен Remnawave не задан");

        var response = await CommonUtils.Web.SendAsync(
            _httpClientFactory, "Remnawave", Global.Vars.Cfg.remnaBaseUrl, method, route, Timeout,
            request =>
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Global.Vars.Cfg.remnaToken);
                request.Headers.Add("X-Forwarded-For", "127.0.0.1");
                request.Headers.Add("X-Forwarded-Proto", "https");

                if (json is not null)
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            },
            cancellationToken);

        if (!response.Ok)
            return RemnaResult<T>.Fail(response.Status is null
                ? response.Error
                : $"{response.Error}: {CommonUtils.Text.Truncate(response.Body, 300)}");

        try
        {
            var envelope = JsonSerializer.Deserialize(response.Body, typeInfo);
            return envelope is null
                ? RemnaResult<T>.Fail("пустой ответ")
                : new RemnaResult<T>(envelope.Response, null);
        }
        catch (JsonException ex)
        {
            return RemnaResult<T>.Fail($"Кривой JSON: {ex.Message}");
        }
    }
}
