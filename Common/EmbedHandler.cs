using Discord;

namespace sblngavnav6.Common
{
    public readonly record struct EmbedFieldSpec(string Name, string Value, bool Inline = false);

    public sealed record EmbedSpec
    {
        public string Title { get; init; }
        public string Url { get; init; }
        public string Description { get; init; }
        public Color? Color { get; init; }

        public string AuthorName { get; init; }
        public string AuthorIconUrl { get; init; }
        public string AuthorUrl { get; init; }

        public string Footer { get; init; }
        public string FooterIconUrl { get; init; }

        public string ThumbnailUrl { get; init; }
        public string ImageUrl { get; init; }

        public bool Timestamp { get; init; }
        public IReadOnlyList<EmbedFieldSpec> Fields { get; init; }
    }

    public static class EmbedHandler
    {
        public const string Name = "sbln";
        public const string MusicTag = "muzik🎸🎧";
        public const string VoteTag = "ultra-выбератор🤔⚡";
        public const string PpmTag = "PPM🏃📦";
        public const string AudioEngine = "Audio8";

        public const int MaxTitle = 256;
        public const int MaxDescription = 4096;
        public const int MaxFooter = 2048;
        public const int MaxAuthor = 256;
        public const int MaxFieldName = 256;
        public const int MaxFieldValue = 1024;
        public const int MaxFields = 25;
        public const int MaxTotal = 6000;

        public static readonly Color DefaultColor = Color.DarkPurple;

        public static string MusicFooter => $"{Name} {MusicTag}";
        public static string VoteFooter => $"{Name} {VoteTag}";
        public static string PpmFooter => $"{Name} {PpmTag}";

        public static EmbedBuilder Builder(EmbedSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);

            var builder = new EmbedBuilder().WithColor(spec.Color ?? DefaultColor);

            if (!string.IsNullOrWhiteSpace(spec.Title))
                builder.WithTitle(Fit(spec.Title, MaxTitle));

            if (Link(spec.Url) is { } url)
                builder.WithUrl(url);

            if (!string.IsNullOrWhiteSpace(spec.AuthorName))
            {
                var author = new EmbedAuthorBuilder().WithName(Fit(spec.AuthorName, MaxAuthor));

                if (Link(spec.AuthorIconUrl) is { } authorIcon)
                    author.WithIconUrl(authorIcon);

                if (Link(spec.AuthorUrl) is { } authorUrl)
                    author.WithUrl(authorUrl);

                builder.WithAuthor(author);
            }

            if (!string.IsNullOrWhiteSpace(spec.Footer))
            {
                var footer = new EmbedFooterBuilder().WithText(Fit(spec.Footer, MaxFooter));

                if (Link(spec.FooterIconUrl) is { } footerIcon)
                    footer.WithIconUrl(footerIcon);

                builder.WithFooter(footer);
            }

            if (Link(spec.ThumbnailUrl) is { } thumbnail)
                builder.WithThumbnailUrl(thumbnail);

            if (Link(spec.ImageUrl) is { } image)
                builder.WithImageUrl(image);

            if (spec.Timestamp)
                builder.WithCurrentTimestamp();

            if (spec.Fields is { Count: > 0 })
            {
                foreach (var field in spec.Fields.Take(MaxFields))
                {
                    builder.AddField(
                        Fit(Fallback(field.Name, "-"), MaxFieldName),
                        Fit(Fallback(field.Value, "-"), MaxFieldValue),
                        field.Inline);
                }
            }

            if (!string.IsNullOrWhiteSpace(spec.Description))
                builder.WithDescription(Fit(spec.Description, DescriptionBudget(builder)));

            return builder;
        }

        public static Embed Build(EmbedSpec spec) => Builder(spec).Build();

        public static Task<Embed> BuildAsync(EmbedSpec spec) => Task.FromResult(Build(spec));

        private static int DescriptionBudget(EmbedBuilder builder)
        {
            var used = (builder.Title?.Length ?? 0)
                     + (builder.Footer?.Text?.Length ?? 0)
                     + (builder.Author?.Name?.Length ?? 0)
                     + builder.Fields.Sum(field => (field.Name?.ToString()?.Length ?? 0) + (field.Value?.ToString()?.Length ?? 0));

            return Math.Clamp(MaxTotal - used, 0, MaxDescription);
        }

        private static string Fit(string value, int limit)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= limit)
                return value;

            return limit <= 1 ? value[..limit] : value[..(limit - 1)] + "…";
        }

        public static string Link(string url)
        {
            url = url?.Trim();

            if (string.IsNullOrEmpty(url))
                return null;

            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("attachment://", StringComparison.OrdinalIgnoreCase)
                ? url
                : null;
        }

        private static string Fallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string Title(string tag, string sub)
            => string.IsNullOrEmpty(sub) ? $"{Name} {tag}" : $"{Name} {tag}, {sub}";

        public static Task<Embed> Music(string sub, string description, Color color)
            => CreateMusicEmbed(Title(MusicTag, sub), description, color);

        public static Task<Embed> MusicError(string sub, string error)
            => CreateErrorEmbed(Title(MusicTag, sub), error);

        public static Task<Embed> MusicCustom(string sub, string description, string footer, Color color)
            => CreateCustomMusicEmbed(Title(MusicTag, sub), description, footer, color);

        public static Task<Embed> CreateMusicEmbed(string title, string description, Color color)
            => BuildAsync(new EmbedSpec
            {
                Title = title,
                Description = description,
                Color = color,
                Footer = $"powered by {AudioEngine}"
            });

        public static Task<Embed> CreateCustomMusicEmbed(string title, string description, string footer, Color color)
            => BuildAsync(new EmbedSpec
            {
                Title = title,
                Description = description,
                Color = color,
                Footer = $"{footer} / powered by {AudioEngine}"
            });

        public static Task<Embed> CreateErrorEmbed(string source, string error)
            => BuildAsync(new EmbedSpec
            {
                Title = $"ОШИБКА ПОСТУПИЛА ИЗ - {source}",
                Description = $"**детали**: \n{error}",
                Color = Discord.Color.DarkRed,
                Footer = "UNIERR-HANDLER"
            });

        public static Task<Embed> CreateFImgEmbed(string description, string url)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            return BuildAsync(new EmbedSpec
            {
                AuthorName = "sbln картинки🧑‍🎨",
                Description = description,
                Color = new Color(255, 166, 207),
                ImageUrl = url
            });
        }

        public static Embed Simple(string title, string description, Color color, string footer = Name)
            => Build(new EmbedSpec
            {
                Title = title,
                Description = description,
                Color = color,
                Footer = footer
            });

        public static Embed Authored(string authorName, string description, Color color, string footer, string authorIconUrl = null)
            => Build(new EmbedSpec
            {
                AuthorName = authorName,
                Description = description,
                Color = color,
                Footer = footer,
                FooterIconUrl = authorIconUrl
            });

        public static Embed Moderation(string title, string description, string thumbnailUrl, string byUser, string byIconUrl)
            => Build(new EmbedSpec
            {
                Title = title,
                Description = description,
                Color = Discord.Color.DarkRed,
                ThumbnailUrl = thumbnailUrl,
                Footer = $"приговор вынес {byUser}",
                FooterIconUrl = byIconUrl,
                Timestamp = true
            });

        public static EmbedBuilder FieldsEmbed(string authorName, Color color, string footer, string thumbnailUrl = null, string authorIconUrl = null)
            => Builder(new EmbedSpec
            {
                AuthorName = authorName,
                AuthorIconUrl = authorIconUrl,
                Color = color,
                Footer = footer,
                ThumbnailUrl = thumbnailUrl
            });
    }
}
