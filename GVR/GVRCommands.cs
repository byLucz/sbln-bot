using Discord;
using Discord.Commands;
using sblngavnav6.Core;
using sblngavnav6.Common;
using sblngavnav6.Data;

namespace sblngavnav6.GVR
{
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

        public GVRCommands(GVRConfig config, CommandHandler commandHandler, GVRDb db)
        {
            _config = config;
            _commandHandler = commandHandler;
            _db = db;
        }

        [Command("говорилка"), Alias("говор")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SendHelp()
        {
            (string Name, string About)[] commands =
            [
                ("доб+", "добавляет сообщения, переписывая всё что было в файле"),
                ("доб", "добавляет сообщения к уже накопленным"),
                ("чистись", "прогоняет базу через фильтры и чистит дубли"),
                ("настройкиговора", "показывает настройки нейросетки"),
                ("шаг", "изменение шагов цепей рандома"),
                ("числов", "число слов в сообщении на выдаче"),
                ("шанс", "шанс что говорилка пропиздиться, роллится на каждое сообщение"),
                ("сообщкол", "количество сообщений для подзагрузки"),
                ("вр", "интервал через который произойдёт подзагрузка"),
                ("верни", "включает особый режим вербальной нищеты **(идея шефа)**"),
                ("сброс", "сброс настроек на дефолт")
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

        [Command("настройкиговора")]
        [RequireUserPermission(GuildPermission.Administrator)]
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

        [Command("добавить", RunMode = RunMode.Async), Alias("доб")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public Task AppendData(uint amount) => CollectAsync(amount, append: true);

        [Command("добавить+", RunMode = RunMode.Async), Alias("доб+")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public Task SeedFile(uint amount) => CollectAsync(amount, append: false);

        [Command("время"), Alias("вр")]
        [RequireUserPermission(GuildPermission.Administrator)]
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

        [Command("чистись", RunMode = RunMode.Async), Alias("чист")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ClearFile()
        {
            var (before, _) = await _db.StampAsync();

            if (before == 0)
            {
                await FailAsync("база пуста, собери сообщения через `доб`");
                return;
            }

            await _db.CleanupAsync();

            var kept = (await _db.LoadAsync())
                .Select(GVRText.Sanitize)
                .Where(line => line is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var after = await _db.ReplaceAsync(0, kept);
            var removed = before - after;

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
        [RequireUserPermission(GuildPermission.Administrator)]
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

        [Command("числослов"), Alias("числов")]
        [RequireUserPermission(GuildPermission.Administrator)]
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
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetChance(uint chance)
        {
            _config.Chance = Math.Min(chance, 100);
            await SaveAsync();
            await DoneAsync("шанс выдачи", $"**{_config.Chance}%**");
        }

        [Command("вербальная нищета"), Alias("верни")]
        [RequireUserPermission(GuildPermission.Administrator)]
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

        [Command("сообщкол")]
        [RequireUserPermission(GuildPermission.Administrator)]
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
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task Reset()
        {
            _config.Reset();
            _commandHandler.UpdateTimerInterval(_config.IntervalMs);
            await SaveAsync();

            await DoneAsync("настройки", "**сброшены на дефолтыч**");
        }

        private async Task CollectAsync(uint amount, bool append)
        {
            if (amount is 0 or > 1000)
            {
                await FailAsync("от 1 до 1000 сообщений за раз");
                return;
            }

            var collected = new List<string>((int)amount);

            await foreach (var message in Context.Channel.GetMessagesAsync((int)amount).Flatten())
            {
                if (message.Author.IsBot)
                    continue;

                var content = Keep(message.Content);

                if (content is not null)
                    collected.Add(content);
            }

            var stored = append
                ? await _db.AddAsync(Context.Guild.Id, collected)
                : await _db.ReplaceAsync(Context.Guild.Id, collected);

            var (total, _) = await _db.StampAsync();

            await DoneAsync(
                append ? "добавлено" : "перезаписано",
                $"**{stored}** из **{collected.Count}** подходящих, в базе **{total}**");
        }

        private static string Keep(string content)
        {
            if (string.IsNullOrWhiteSpace(content) ||
                content.StartsWith(Global.Vars.Cfg.pref1, StringComparison.OrdinalIgnoreCase) ||
                content.StartsWith(Global.Vars.Cfg.pref2, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return GVRText.Sanitize(content);
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
