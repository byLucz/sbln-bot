using System.Collections.Concurrent;
using TwitchLib.Api.Interfaces;

namespace sblngavnav6.TwitchService
{
    public class StreamData
    {
        public string Stream { get; set; }
        public string Id { get; set; }
        public string Avatar { get; set; }
        public string Title { get; set; }
        public string Thumb { get; set; }
        public string Game { get; set; }
        public int Viewers { get; set; }
        public string Link { get; set; }
    }

    public abstract class StreamMonoServiceBase
    {
        protected int CreationAttempts { get; set; }

        public ITwitchAPI TwitchApi { get; protected set; }

        protected const int UpdInt = 600;

        public List<string> StreamList { get; protected set; } = [];

        public List<string> StreamIdList { get; protected set; } = [];

        public ConcurrentDictionary<string, byte> StreamsOnline { get; } = new();

        public Dictionary<string, string> StreamIds { get; protected set; } = [];

        protected Dictionary<string, StreamData> StreamModels { get; set; } = [];

        protected Dictionary<string, string> StreamProfileImages { get; set; } = [];
    }
}
