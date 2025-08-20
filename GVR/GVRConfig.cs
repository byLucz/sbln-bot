namespace sblngavnav5X.GVR
{
    public class GuildConfig
    {
        public GovorConfig govorilka { get; set; } = new GovorConfig();
    }
    public class GovorConfig
    {
        public uint Step { get; set; } = 1;
        public int Count { get; set; } = 10;
        public uint Collection { get; set; } = 100;
        public uint Chance { get; set; } = 5;
        public bool Rand { get; set; } = true;
        public bool VerbalAbuseBySheff { get; set; } = false;

    }
}
