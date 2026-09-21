using System.Globalization;
using System.Text.Json;
using Telegram.Bot.Types;

namespace sblngavnav6.TelegramExtensions.Services;

public sealed record ChatStatistics(DateTime ObservedSinceUtc, long ReceivedMessages = 0,
    int LastMessageId = 0, DateTime? CreatedAtUtc = null);

public sealed class ChatStatisticsStore(string dataDirectory) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(dataDirectory), "telegram", "chats");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ChatStatistics> ObserveAsync(long botId, Message message, CancellationToken cancellationToken)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{botId}_{message.Chat.Id}.json");
        var path = Path.Combine(_directory, name);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            ChatStatistics? previous = null;
            if (File.Exists(path))
            {
                await using var source = File.OpenRead(path);
                previous = await JsonSerializer.DeserializeAsync<ChatStatistics>(source, cancellationToken: cancellationToken)
                    ?? throw new JsonException("Chat statistics file is empty.");
            }

            var statistics = previous ?? new ChatStatistics(DateTime.UtcNow);
            var creation = IsCreation(message) ? message : message.ReplyToMessage is { } reply &&
                reply.Chat.Id == message.Chat.Id && IsCreation(reply) ? reply : null;
            if (creation is not null && statistics.CreatedAtUtc is null)
                statistics = statistics with { CreatedAtUtc = creation.Date };
            if (message.Id > statistics.LastMessageId)
                statistics = statistics with
                {
                    ReceivedMessages = statistics.ReceivedMessages + 1,
                    LastMessageId = message.Id
                };
            if (statistics == previous) return statistics;

            var temporaryPath = path + ".tmp";
            try
            {
                await using (var target = File.Create(temporaryPath))
                    await JsonSerializer.SerializeAsync(target, statistics, cancellationToken: cancellationToken);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            return statistics;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsCreation(Message message)
        => message.GroupChatCreated == true || message.SupergroupChatCreated == true || message.ChannelChatCreated == true;

    public void Dispose() => _gate.Dispose();
}
