using System.Collections.Generic;

// Lobide seçilen roller, world sahnesine geçerken burada taşınır (sadece sunucuda dolu).
public static class MatchSettings
{
    public struct PlayerInfo
    {
        public string Name;
        public bool IsHarvester;
    }

    public static readonly Dictionary<ulong, PlayerInfo> Players = new Dictionary<ulong, PlayerInfo>();
}
