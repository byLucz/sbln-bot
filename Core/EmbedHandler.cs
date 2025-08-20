using Discord;
using System;

namespace sblngavnav5X.Core
{
    public static class EmbedHandler
    {
        public static async Task<Embed> CreateMusicEmbed(string title, string description, Color color)
        {
            var embed = await Task.Run(() => (new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter("L4 + V7 open beta")
                .WithCurrentTimestamp().Build()));
            return embed;
        }

        public static async Task<Embed> CreateErrorEmbed(string source, string error)
        {
            var embed = await Task.Run(() => new EmbedBuilder()
                .WithTitle($"ОШИБКА ПОСТУПИЛА ИЗ - {source}")
                .WithDescription($"**детали**: \n{error}")
                .WithColor(Color.DarkRed)
                .WithCurrentTimestamp().Build());
            return embed;
        }

        public static async Task<Embed> CreateFImgEmbed(string description, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("URL пустое", nameof(url));
            }

            var embed = await Task.Run(() => (new EmbedBuilder()
            .WithAuthor("sbln милашки\U0001f97a👉🏻👈🏻")
            .WithDescription(description)
            .WithColor(new Color(255, 166, 207))
            .WithImageUrl(url)
            .Build()));
            return embed;
        }
    }
}
