using sblngavnav6.Common;
using sblngavnav6.Services;
using System.Text.Json;

namespace sblngavnav6.Remna;

public sealed class RemnaStore
{
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(
        Environment.GetEnvironmentVariable("SBLN_DATA_DIR") is { Length: > 0 } dir ? dir : AppContext.BaseDirectory,
        "remna.json");
    private RemnaState _state;

    public async Task<RemnaMaintenance> MaintenanceAsync(RemnaServer server, string uuid)
        => await ReadAsync(state => state.Maintenance.FirstOrDefault(m => m.Server == server.Url && m.Uuid == uuid));

    public Task<List<RemnaMaintenance>> AllMaintenanceAsync()
        => ReadAsync(state => state.Maintenance.ToList());

    public Task<List<RemnaIncident>> IncidentsAsync()
        => ReadAsync(state => state.Incidents
            .Where(i => i.End is null || i.End > DateTimeOffset.UtcNow - HistoryWindow)
            .OrderByDescending(i => i.Start)
            .ToList());

    public Task StartMaintenanceAsync(RemnaMaintenance entry) => WriteAsync(state =>
    {
        state.Maintenance.RemoveAll(m => m.Server == entry.Server && m.Uuid == entry.Uuid);
        state.Maintenance.Add(entry);
        return true;
    });

    public async Task<RemnaMaintenance> EndMaintenanceAsync(RemnaServer server, string uuid)
    {
        RemnaMaintenance removed = null;
        await WriteAsync(state =>
        {
            removed = state.Maintenance.FirstOrDefault(m => m.Server == server.Url && m.Uuid == uuid);
            return removed is not null && state.Maintenance.Remove(removed);
        });
        return removed;
    }

    public Task OpenIncidentAsync(RemnaIncident incident) => WriteAsync(state =>
    {
        if (state.Incidents.Any(i => i.Server == incident.Server && i.Uuid == incident.Uuid && i.End is null))
            return false;
        state.Incidents.Add(incident);
        return true;
    });

    public async Task<RemnaIncident> CloseIncidentAsync(RemnaServer server, string uuid, DateTimeOffset end)
    {
        RemnaIncident closed = null;
        await WriteAsync(state =>
        {
            closed = state.Incidents.FirstOrDefault(i => i.Server == server.Url && i.Uuid == uuid && i.End is null);
            if (closed is null)
                return false;
            closed.End = end;
            return true;
        });
        return closed;
    }

    public Task<bool> HasOpenIncidentAsync(RemnaServer server, string uuid)
        => ReadAsync(state => state.Incidents.Any(i => i.Server == server.Url && i.Uuid == uuid && i.End is null));

    private async Task<T> ReadAsync<T>(Func<RemnaState, T> read)
    {
        await _gate.WaitAsync();
        try
        {
            return read(Load());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteAsync(Func<RemnaState, bool> change)
    {
        await _gate.WaitAsync();
        try
        {
            var state = Load();
            if (!change(state))
                return;
            state.Incidents.RemoveAll(i => i.End is { } end && end < DateTimeOffset.UtcNow - HistoryWindow);

            var temp = _path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, RemnaJsonContext.Default.RemnaState));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (CommonUtils.IsIoFailure(ex))
        {
            await LoggingService.LogErrorAsync("REMNA", $"Не удалось сохранить {_path}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private RemnaState Load()
    {
        if (_state is not null)
            return _state;

        try
        {
            _state = File.Exists(_path)
                ? JsonSerializer.Deserialize(File.ReadAllText(_path), RemnaJsonContext.Default.RemnaState) ?? new RemnaState()
                : new RemnaState();
        }
        catch (Exception ex) when (ex is JsonException || CommonUtils.IsIoFailure(ex))
        {
            _ = LoggingService.LogErrorAsync("REMNA", $"Не удалось прочитать {_path}, начинаю с пустого состояния", ex);
            _state = new RemnaState();
        }

        return _state;
    }
}
