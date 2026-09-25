namespace sblngavnav6.GVR
{
    public class GovorConfig
    {
        public uint Step { get; set; } = 1;
        public int Count { get; set; } = 10;
        public uint Collection { get; set; } = 100;
        public uint Chance { get; set; } = 5;
        public bool Rand { get; set; } = true;
        public bool VerbalAbuseBySheff { get; set; } = false;

        public void Reset()
        {
            var defaults = new GovorConfig();
            Step = defaults.Step;
            Count = defaults.Count;
            Collection = defaults.Collection;
            Chance = defaults.Chance;
            Rand = defaults.Rand;
            VerbalAbuseBySheff = defaults.VerbalAbuseBySheff;
        }
    }
}
