using System.Reflection;

namespace sblngavnav6.Common
{
    public readonly record struct PackageVersion(string Name, string Version);

    public static class Versioning
    {
        private static string Meta(string key)
            => typeof(Versioning).Assembly
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                .Cast<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value ?? "";

        private static string ParseCommit()
        {
            var info = typeof(Versioning).Assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .Cast<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "";
            var plus = info.IndexOf('+');
            if (plus < 0 || plus + 1 >= info.Length) return "";
            var sha = info[(plus + 1)..];
            return sha.Length > 7 ? sha[..7] : sha;
        }

        public static readonly string Version = Meta("SblnVersion") is { Length: > 0 } v ? v : "0.0.0";
        public static readonly string Channel = Meta("SblnChannel");
        public static readonly string Commit = ParseCommit();
        public static readonly string Full =
            $"v{Version}"
            + (string.IsNullOrEmpty(Channel) ? "" : $" {Channel}")
            + (string.IsNullOrEmpty(Commit) ? "" : $" / {Commit}");

        public static IReadOnlyList<PackageVersion> Packages { get; } = BuildPackages();

        public static string Of(Type marker) => Describe(marker.Assembly);

        private static List<PackageVersion> BuildPackages()
        {
            var lavalink = new (string Name, Type Marker)[]
            {
                ("Lavalink4NET", typeof(Lavalink4NET.IAudioService)),
                ("Lavalink4NET.Discord.NET", typeof(Lavalink4NET.DiscordNet.DiscordClientWrapper)),
                ("Lavalink4NET.InactivityTracking", typeof(Lavalink4NET.InactivityTracking.IInactivityTrackingService))
            };

            var markers = new (string Name, Type Marker)[]
            {
                ("Discord.Net", typeof(Discord.WebSocket.DiscordSocketClient)),
                ("NAudio", typeof(NAudio.Wave.WaveFileWriter)),
                ("NLayer", typeof(NLayer.MpegFile)),
                ("MailKit", typeof(MailKit.Net.Imap.ImapClient)),
                ("MySqlConnector", typeof(MySqlConnector.MySqlConnection)),
                ("TwitchLib.Api", typeof(TwitchLib.Api.TwitchAPI)),
                ("Telegram.Bot", typeof(Telegram.Bot.TelegramBotClient))
            };

            var packages = new List<PackageVersion>(markers.Length + lavalink.Length);

            packages.Add(Resolve("Discord.Net", markers[0].Marker));
            packages.AddRange(Group("Lavalink4NET", lavalink));

            foreach (var (name, marker) in markers.Skip(1))
                packages.Add(Resolve(name, marker));

            return packages;
        }

        private static IEnumerable<PackageVersion> Group(string name, (string Name, Type Marker)[] parts)
        {
            var resolved = parts.Select(part => Resolve(part.Name, part.Marker)).ToArray();
            var versions = resolved.Select(item => item.Version).Distinct().ToArray();

            return versions.Length == 1
                ? [new PackageVersion(name, versions[0])]
                : resolved;
        }

        private static PackageVersion Resolve(string name, Type marker)
        {
            try { return new PackageVersion(name, Describe(marker.Assembly)); }
            catch (Exception) { return new PackageVersion(name, "недоступно"); }
        }

        private static string Describe(Assembly assembly)
        {
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .Cast<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "неизвестно";
        }
    }
}
