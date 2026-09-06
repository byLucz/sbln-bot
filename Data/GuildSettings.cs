namespace sblngavnav5X.Data
{
    public sealed class GuildSettings
    {
        public ulong GuildId { get; set; }
        public ulong? SuperuserRoleId { get; set; }
        public ulong? WelcomeChannelId { get; set; }
        public string WelcomeMessage { get; set; }
        public ulong? WelcomeRoleId { get; set; }
        public ulong? StreamNotifChannelId { get; set; }
    }
}
