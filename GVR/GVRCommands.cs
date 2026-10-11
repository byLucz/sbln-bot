using Discord;
using Discord.Commands;
using sblngavnav6.Common;
using sblngavnav6.Core;

namespace sblngavnav6.GVR
{
    [Group("говор")]
    [Alias("говорилка", "гвр")]
    [RequireDevGuild]
    [RequireUserPermission(GuildPermission.Administrator)]
    public class GVRCommands : ModuleBase<SocketCommandContext>
    {
        private const string Author = "sbln говорилка🎤📓";
        private const string Footer = "powered by GovorNGN";
        private const string Icon = "https://assets.piliapp.com/s3pxy/emoji/meaning/preview/brain.png?polish=2";
        private const string Source = "говорилка";

        private static readonly Color Tint = Color.LighterGrey;

        private readonly GVRConfig _config;
        private readonly CommandHandler _commandHandler;
        private readonly GVRDb _db;
        private readonly GVRMessagesHandler _messages;

        public GVRCommands(GVRConfig config, CommandHandler commandHandler, GVRDb db, GVRMessagesHandler messages)
        {
            _config = config;
            _commandHandler = commandHandler;
            _db = db;
            _messages = messages;
        }

        [Command]
        public async Task SendHelp()
        {
            (string Name, string About)[] commands =
            [
                ("говор доб <кол-во>", "берёт последние N сообщений канала, добавляет подходящие и чистит базу"),
                ("говор чист", "прогоняет базу через фильтры и чистит дубли"),
                ("говор настройки", "показывает настройки нейросетки"),
                ("говор шаг <1-15>", "изменение шагов цепей рандома"),
                ("говор слов <3-50|рандом>", "число слов в сообщении на выдаче"),
                ("говор шанс <0-100>", "шанс что говорилка пропиздиться, роллится на каждое сообщение"),
                ("говор кол <0-300>", "сколько последних сообщений берёт автосбор"),
                ("говор вр <мс>", "интервал через который произойдёт подзагрузка"),
                ("говор нищета <вкл|выкл>", "включает особый режим вербальной нищеты **(идея шефа)**"),
                ("говор сброс", "сброс настроек на дефолт")
            ];

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Author,
                AuthorIconUrl = Icon,
                Title = "комманды для лютой нейросетки",
                Color = Color.DarkPurple,
                Fields = commands.Select(item => new EmbedFieldSpec(item.Name, item.About)).ToArray(),
                Footer = Footer
            }));
        }

        [Command("настройки")]
        public async Task GetSettings()
        {
            var words = _config.Rand ? "рандом" : _config.Count.ToString();

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = "sbln говорилка/настройки🎤📓",
                AuthorIconUrl = Icon,
                Color = Tint,
                Fields =
                [
                    new EmbedFieldSpec("шаг рандома", $"**{_config.Step}**", true),
                    new EmbedFieldSpec("число слов", $"**{words}**", true),
                    new EmbedFieldSpec("шанс ролла", $"**{_config.Chance}%**", true),
                    new EmbedFieldSpec("сообщений подзагрузки", $"**{_config.Collection}**", true),
                    new EmbedFieldSpec("время подзагрузки", $"**{_commandHandler.GetTimerInterval() / 1000.0:0.##} сек**", true),
                    new EmbedFieldSpec("вербальная нищета", _config.VerbalAbuseBySheff ? "**вкл**" : "**выкл**", true)
                ],
                Footer = Footer
            }));
        }

        [Command("добавить"), Alias("доб")]
        public async Task AppendData(uint amount)
        {
            if (amount is 0 or > 1000)
            {
                await FailAsync("от 1 до 1000 сообщений за раз");
                return;
            }

            using (Context.Channel.EnterTypingState())
            {
                var (added, images, scanned, removed, total) = await _messages.CollectAsync(Context.Channel, (int)amount);

                await DoneAsync(
                    "добавлено",
                    $"**{added}** новых, картинок **{images}**, просмотрено **{scanned}**, вычищено **{removed}**, в базе **{total}**");
            }
        }

        [Command("время"), Alias("вр")]
        public async Task TimeMS(int amount)
        {
            if (amount <= 0)
            {
                await FailAsync("укажи значение больше нуля");
                return;
            }

            _commandHandler.UpdateTimerInterval(amount);
            _config.IntervalMs = amount;
            await SaveAsync();
            await DoneAsync("время подзагрузки", $"**{amount / 1000.0:0.##} сек**");
        }

        [Command("чистись"), Alias("чист")]
        public async Task ClearFile()
        {
            var (before, _) = await _db.StampAsync();

            if (before == 0)
            {
                await FailAsync("база пуста, собери сообщения через `говор доб`");
                return;
            }

            var removed = await _db.PruneAsync();
            var (after, _) = await _db.StampAsync();

            await ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Author,
                Color = Tint,
                Fields =
                [
                    new EmbedFieldSpec("было строк", $"**{before}**", true),
                    new EmbedFieldSpec("осталось", $"**{after}**", true),
                    new EmbedFieldSpec("удалено говна", $"**{removed}**", true)
                ],
                Footer = Footer
            }));
        }

        [Command("шаг")]
        public async Task SetStep(uint step)
        {
            if (step is < 1 or > 15)
            {
                await FailAsync("шаг в диапазоне от 1 до 15");
                return;
            }

            _config.Step = step;
            await SaveAsync();
            await DoneAsync("шаг", $"**{step}**");
        }

        [Command("слова"), Alias("слов")]
        public async Task SetCount(string value)
        {
            if (value.Equals("рандом", StringComparison.OrdinalIgnoreCase) || value == "-")
            {
                _config.Rand = true;
                await SaveAsync();
                await DoneAsync("число слов", "**рандом**");
                return;
            }

            if (!int.TryParse(value, out var count) || count is < 3 or > 50)
            {
                await FailAsync("число слов в диапазоне от 3 до 50, либо `рандом`");
                return;
            }

            _config.Rand = false;
            _config.Count = count;
            await SaveAsync();
            await DoneAsync("число слов", $"**{count}**");
        }

        [Command("шанс")]
        public async Task SetChance(uint chance)
        {
            _config.Chance = Math.Min(chance, 100);
            await SaveAsync();
            await DoneAsync("шанс выдачи", $"**{_config.Chance}%**");
        }

        [Command("нищета"), Alias("верни", "вербальная нищета")]
        public async Task VerbalAbuse(string mode)
        {
            var enabled = mode?.Trim().ToLowerInvariant() switch
            {
                "вкл" or "on" or "1" => true,
                "выкл" or "off" or "0" => false,
                _ => (bool?)null
            };

            if (enabled is null)
            {
                await FailAsync("только `вкл` или `выкл`");
                return;
            }

            _config.VerbalAbuseBySheff = enabled.Value;
            await SaveAsync();
            await DoneAsync("вербальная нищета", enabled.Value ? "**вкл**" : "**выкл**");
        }

        [Command("кол"), Alias("сообщкол")]
        public async Task SetCollection(uint amount)
        {
            if (amount > 300)
            {
                await FailAsync("не больше 300");
                return;
            }

            _config.Collection = amount;
            await SaveAsync();
            await DoneAsync("сообщений подзагрузки", $"**{amount}**");
        }

        [Command("сброс")]
        public async Task Reset()
        {
            _config.Reset();
            _commandHandler.UpdateTimerInterval(_config.IntervalMs);
            await SaveAsync();

            await DoneAsync("настройки", "**сброшены на дефолтыч**");
        }

        private Task SaveAsync() => _db.SaveSettingsAsync(_config);

        private Task DoneAsync(string what, string value) =>
            ReplyAsync(embed: EmbedHandler.Build(new EmbedSpec
            {
                AuthorName = Author,
                Description = $"{what}: {value}",
                Color = Tint,
                Footer = Footer
            }));

        private async Task FailAsync(string reason) =>
            await ReplyAsync(embed: await EmbedHandler.CreateErrorEmbed(Source, reason));
    }
}
