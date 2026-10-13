using sblngavnav6.Common;
using sblngavnav6.Data;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
    private static readonly TimeSpan AgentTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AgentHealthTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan AgentCleanTimeout = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan HostsLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, RemnaResult<List<RemnaHost>> Result)> _hosts = new();

    private readonly IHttpClientFactory _httpClientFactory;

    public RemnaService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IReadOnlyList<RemnaServer> Servers { get; } = Global.Vars.Cfg.remnaServers
        .Select((server, index) => new RemnaServer(
            index,
            !string.IsNullOrWhiteSpace(server.Name) ? server.Name
                : Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ? uri.Host : server.Url,
            server.Url,
            server.Token))
        .ToList();

    public bool Multi => Servers.Count > 1;

    public RemnaServer Server(int index) => index >= 0 && index < Servers.Count ? Servers[index] : null;

    public RemnaServer Server(string index) => Server(int.TryParse(index, out var value) ? value : -1);

    public async Task<List<RemnaServerNodes>> AllNodesAsync(CancellationToken cancellationToken = default)
        => (await Task.WhenAll(Servers.Select(async server => new RemnaServerNodes(server, await NodesAsync(server, cancellationToken))))).ToList();

    public Task<RemnaResult<List<RemnaNode>>> NodesAsync(RemnaServer server, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Get, "/api/nodes", RemnaJsonContext.Default.RemnaEnvelopeListRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> NodeAsync(RemnaServer server, string uuid, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Get, $"/api/nodes/{Uri.EscapeDataString(uuid)}", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> EnableNodeAsync(RemnaServer server, string uuid, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/enable", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaNode>> DisableNodeAsync(RemnaServer server, string uuid, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/disable", RemnaJsonContext.Default.RemnaEnvelopeRemnaNode, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaEvent>> RestartNodeAsync(RemnaServer server, string uuid, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Post, $"/api/nodes/{Uri.EscapeDataString(uuid)}/actions/restart", RemnaJsonContext.Default.RemnaEnvelopeRemnaEvent, RestartBody, cancellationToken);

    public Task<RemnaResult<RemnaEvent>> RestartAllAsync(RemnaServer server, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Post, "/api/nodes/actions/restart-all", RemnaJsonContext.Default.RemnaEnvelopeRemnaEvent, RestartBody, cancellationToken);

    public async Task<RemnaResult<List<RemnaHost>>> HostsAsync(RemnaServer server, CancellationToken cancellationToken = default)
    {
        if (_hosts.TryGetValue(server.Url, out var cached) && DateTimeOffset.UtcNow - cached.At < HostsLifetime)
            return cached.Result;

        var result = await SendAsync(server, HttpMethod.Get, "/api/hosts", RemnaJsonContext.Default.RemnaEnvelopeListRemnaHost, cancellationToken: cancellationToken);
        if (result.Ok)
            _hosts[server.Url] = (DateTimeOffset.UtcNow, result);
        return result;
    }

    public static bool AgentConfigured => !string.IsNullOrWhiteSpace(Global.Vars.Cfg.remnaAgentToken);

    public Task<RemnaResult<RemnaScan>> ScanAsync(RemnaNode node, string current, CancellationToken cancellationToken = default)
        => AgentAsync(node, $"/scan?top=10{(string.IsNullOrWhiteSpace(current) ? "" : $"&current={Uri.EscapeDataString(current)}")}",
            AgentTimeout, RemnaJsonContext.Default.RemnaScan, cancellationToken);

    public Task<RemnaResult<RemnaAgentClean>> AgentCleanAsync(RemnaNode node, CancellationToken cancellationToken = default)
        => AgentAsync(node, "/clean", AgentCleanTimeout, RemnaJsonContext.Default.RemnaAgentClean, cancellationToken);

    public Task<RemnaResult<RemnaAgentHealth>> AgentHealthAsync(RemnaNode node, CancellationToken cancellationToken = default)
        => AgentAsync(node, "/health", AgentHealthTimeout, RemnaJsonContext.Default.RemnaAgentHealth, cancellationToken);

    private async Task<RemnaResult<T>> AgentAsync<T>(RemnaNode node, string route, TimeSpan timeout, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (!AgentConfigured)
            return RemnaResult<T>.Fail("не задан Remna:AgentToken");

        var baseUri = new UriBuilder("http", node.Address, Global.Vars.Cfg.remnaAgentPort).Uri;
        var response = await CommonUtils.Web.SendAsync(
            _httpClientFactory, "RemnaAgent", baseUri.ToString(),
            HttpMethod.Get, route, timeout,
            request =>
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var path = new Uri(baseUri, route).GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
                var sig = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Global.Vars.Cfg.remnaAgentToken), Encoding.UTF8.GetBytes($"GET\n{path}\n{ts}"));
                request.Headers.Add("X-Ragent-Ts", ts);
                request.Headers.Add("X-Ragent-Sig", Convert.ToHexStringLower(sig));
            },
            cancellationToken);

        if (!response.Ok)
            return RemnaResult<T>.Fail(response.Failure == HttpRequestError.ResponseEnded ? "неверный токен, сбиты часы или агент не обновлён (сброс соединения)"
                : response.Status is null ? $"недоступен: {response.Error}"
                : response.Status == System.Net.HttpStatusCode.Conflict ? "скан уже идёт, подожди пару минут"
                : $"ответил {response.Error}");

        try
        {
            return JsonSerializer.Deserialize(response.Body, typeInfo) is { } data
                ? new RemnaResult<T>(data, null)
                : RemnaResult<T>.Fail("пустой ответ");
        }
        catch (JsonException ex)
        {
            return RemnaResult<T>.Fail($"кривой JSON: {ex.Message}");
        }
    }

    public Task<RemnaResult<RemnaStats>> StatsAsync(RemnaServer server, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Get, "/api/system/stats", RemnaJsonContext.Default.RemnaEnvelopeRemnaStats, cancellationToken: cancellationToken);

    public Task<RemnaResult<RemnaHealth>> HealthAsync(RemnaServer server, CancellationToken cancellationToken = default)
        => SendAsync(server, HttpMethod.Get, "/api/system/health", RemnaJsonContext.Default.RemnaEnvelopeRemnaHealth, cancellationToken: cancellationToken);

    private async Task<RemnaResult<T>> SendAsync<T>(
        RemnaServer server,
        HttpMethod method,
        string route,
        JsonTypeInfo<RemnaEnvelope<T>> typeInfo,
        string json = null,
        CancellationToken cancellationToken = default)
    {
        if (!Global.Vars.Cfg.remnaEnabled)
            return RemnaResult<T>.Fail("Remnawave отключён в конфигурации");

        if (server is null)
            return RemnaResult<T>.Fail("сервер Remnawave не найден, обнови панель");

        if (string.IsNullOrWhiteSpace(server.Token))
            return RemnaResult<T>.Fail($"Токен Remnawave для {server.Name} не задан");

        var response = await CommonUtils.Web.SendAsync(
            _httpClientFactory, "Remnawave", server.Url, method, route, Timeout,
            request =>
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
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

        if (string.IsNullOrWhiteSpace(response.Body))
            return new RemnaResult<T>(default, null);

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
