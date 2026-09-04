using Discord;
using System;

namespace sblngavnav5X.Common
{
    public static class EmbedHandler
    {
        public const string Brand = "sbln";
        public const string MusicTag = "muzik🎸🎧";
        public const string VoteTag = "ultra-выбератор🤔⚡";
        public const string PpmTag = "PPM";

        public static string MusicFooter => $"{Brand} {MusicTag}";
        public static string VoteFooter => $"{Brand} {VoteTag}";
        public static string PpmFooter => $"{Brand} {PpmTag}";

        private static string Title(string tag, string sub)
            => string.IsNullOrEmpty(sub) ? $"{Brand} {tag}" : $"{Brand} {tag}, {sub}";

        public static Task<Embed> Music(string sub, string description, Color color)
            => CreateMusicEmbed(Title(MusicTag, sub), description, color);

        public static Task<Embed> MusicError(string sub, string error)
            => CreateErrorEmbed(Title(MusicTag, sub), error);

        public static Task<Embed> MusicCustom(string sub, string description, string footer, Color color)
            => CreateCustomMusicEmbed(Title(MusicTag, sub), description, footer, color);

        public static async Task<Embed> CreateMusicEmbed(string title, string description, Color color)
        {
            return await Task.Run(() => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter("powered by AudioSeven")
                .Build());
        }

        public static async Task<Embed> CreateCustomMusicEmbed(string title, string description, string footer, Color color)
        {
            return await Task.Run(() => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter(footer + " • powered by AudioSeven")
                .Build());
        }

        public static async Task<Embed> CreateErrorEmbed(string source, string error)
        {
            return await Task.Run(() => new EmbedBuilder()
                .WithTitle($"ОШИБКА ПОСТУПИЛА ИЗ - {source}")
                .WithDescription($"**детали**: \n{error}")
                .WithColor(Color.DarkRed)
                .WithCurrentTimestamp()
                .Build());
        }

        public static async Task<Embed> CreateFImgEmbed(string description, string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL пустое", nameof(url));

            return await Task.Run(() => new EmbedBuilder()
                .WithAuthor("sbln милашки\U0001f97a👉🏻👈🏻")
                .WithDescription(description)
                .WithColor(new Color(255, 166, 207))
                .WithImageUrl(url)
                .Build());
        }

        public static Embed Simple(string title, string description, Color color, string footer = "sbln")
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter(footer)
                .Build();

        public static Embed Authored(string authorName, string description, Color color, string footer, string? footerIconUrl = null)
        {
            var fb = new EmbedFooterBuilder().WithText(footer);
            if (!string.IsNullOrEmpty(footerIconUrl))
                fb.WithIconUrl(footerIconUrl);

            return new EmbedBuilder()
                .WithAuthor(authorName)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter(fb)
                .Build();
        }

        public static Embed Moderation(string title, string description, string thumbnailUrl, string byUser, string byIconUrl)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Color.DarkRed)
                .WithCurrentTimestamp()
                .WithThumbnailUrl(thumbnailUrl)
                .WithFooter(new EmbedFooterBuilder()
                    .WithText($"приговор вынес {byUser}")
                    .WithIconUrl(byIconUrl))
                .Build();

        public static EmbedBuilder FieldsEmbed(string authorName, Color color, string footer, string? thumbnailUrl = null, string? authorIconUrl = null)
        {
            var author = new EmbedAuthorBuilder().WithName(authorName);
            if (!string.IsNullOrEmpty(authorIconUrl))
                author.WithIconUrl(authorIconUrl);

            var b = new EmbedBuilder()
                .WithAuthor(author)
                .WithColor(color)
                .WithFooter(footer);

            if (!string.IsNullOrEmpty(thumbnailUrl))
                b.WithThumbnailUrl(thumbnailUrl);

            return b;
        }
    }
}
