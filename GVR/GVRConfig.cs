namespace sblngavnav6.GVR
{
    public class GVRConfig
    {
        public const int DefaultIntervalMs = 86400000;
        public const string NGNVersion = "2.0";
        public uint Step { get; set; } = 1;
        public int Count { get; set; } = 10;
        public uint Collection { get; set; } = 100;
        public uint Chance { get; set; } = 5;
        public bool Rand { get; set; } = true;
        public bool VerbalAbuseBySheff { get; set; } = false;
        public int IntervalMs { get; set; } = DefaultIntervalMs;

        public void Reset()
        {
            var defaults = new GVRConfig();
            Step = defaults.Step;
            Count = defaults.Count;
            Collection = defaults.Collection;
            Chance = defaults.Chance;
            Rand = defaults.Rand;
            VerbalAbuseBySheff = defaults.VerbalAbuseBySheff;
            IntervalMs = defaults.IntervalMs;
        }
    }
}
