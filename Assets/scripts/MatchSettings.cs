using System.Collections.Generic;

// Lobideki oyuncu isimleri, race sahnesine geçerken burada taşınır (sadece sunucuda dolu).
public static class MatchSettings
{
    public struct PlayerInfo
    {
        public string Name;
    }

    public static readonly Dictionary<ulong, PlayerInfo> Players = new Dictionary<ulong, PlayerInfo>();

    // Tur bitip lobiye dönülünce oda aynı şifreyle açık kalsın
    public static string RoomPassword = "";
}
