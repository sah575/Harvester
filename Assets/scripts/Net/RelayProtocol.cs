// Online oyun için relay sunucusu ile oyun arasındaki ortak protokol.
// Bu dosya hem Unity'de hem de Server/HarvesterRelay projesinde derlenir.
// Mesaj düzenini değiştirirsen Version'ı artır ve sunucuyu yeniden yükle.
using System.Text;

namespace Harvester.Net
{
    /// <summary>İstemcinin bağlanırken sunucudan istediği şey.</summary>
    public enum RelayConnectOp : byte
    {
        Create = 1, // [maxPlayers byte]
        Join = 2,   // [kod string]
    }

    /// <summary>Her relay mesajının ilk baytı.</summary>
    public enum RelayMsg : byte
    {
        RoomCreated = 1, // sunucu → host:     [kod string][maxPlayers byte]
        Joined = 2,      // sunucu → istemci:  [kendi id ushort][kod string]
        PeerJoined = 3,  // sunucu → host:     [id ushort]
        PeerLeft = 4,    // sunucu → host:     [id ushort]
        Data = 5,        // istemci → sunucu:  [veri]            sunucu → host:    [gönderen id ushort][veri]
                         // host → sunucu:     [hedef id ushort][veri]  sunucu → istemci: [veri]
        Kick = 6,        // host → sunucu:     [id ushort]
    }

    /// <summary>Bağlantının reddedilme ya da kesilme sebebi (ayrılma verisinin ilk baytı).</summary>
    public enum RelayError : byte
    {
        None = 0,
        BadRequest = 1,
        VersionMismatch = 2,
        RoomNotFound = 3,
        RoomFull = 4,
        ServerFull = 5,
        TooManyAttempts = 6,
        RoomClosed = 7,
        Kicked = 8,
        RateLimited = 9,
    }

    public static class RelayProtocol
    {
        public const uint Magic = 0x48525659; // "HRVY"
        public const byte Version = 1;

        public const string DefaultAddress = "188.132.198.43";
        public const ushort DefaultPort = 7778;

        public const int MinPlayers = 2;
        public const int MaxPlayersLimit = 8;

        // Karışan karakterler (0/O, 1/I) yok.
        public const int CodeLength = 6;
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public const int DisconnectTimeoutMs = 10000;

        /// <summary>Relay'in güvenilmez paketlere eklediği başlık için ayrılan pay.</summary>
        public const int HeaderReserve = 4;

        /// <summary>Kullanıcının yazdığı kodu büyük harfe çevirir, boşluk ve tireleri atar.</summary>
        public static string NormalizeCode(string raw)
        {
            if (raw == null) return string.Empty;
            var sb = new StringBuilder(CodeLength);
            foreach (char c in raw)
            {
                if (c == ' ' || c == '-' || c == '\t') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        public static bool IsValidCode(string code)
        {
            if (code == null || code.Length != CodeLength) return false;
            foreach (char c in code)
                if (CodeAlphabet.IndexOf(c) < 0) return false;
            return true;
        }

        /// <summary>Hatanın oyuncuya gösterilecek Türkçe açıklaması.</summary>
        public static string Describe(RelayError error)
        {
            switch (error)
            {
                case RelayError.BadRequest: return "Geçersiz istek.";
                case RelayError.VersionMismatch: return "Oyun sürümü sunucuyla uyuşmuyor. Oyunu güncelle.";
                case RelayError.RoomNotFound: return "Bu kodla bir oda bulunamadı.";
                case RelayError.RoomFull: return "Oda dolu.";
                case RelayError.ServerFull: return "Sunucu dolu, biraz sonra tekrar dene.";
                case RelayError.TooManyAttempts: return "Çok fazla hatalı deneme. Bir dakika bekle.";
                case RelayError.RoomClosed: return "Oda kapandı: kurucu oyundan ayrıldı.";
                case RelayError.Kicked: return "Odadan atıldın.";
                case RelayError.RateLimited: return "Çok fazla veri gönderildi, bağlantı kesildi.";
                default: return "Bağlantı kesildi.";
            }
        }
    }
}
