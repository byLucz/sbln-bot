using System.Linq;
using System.Reflection;

namespace sblngavnav5X.Common
{
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
    }
}
