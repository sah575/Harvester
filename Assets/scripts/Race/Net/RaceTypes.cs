using System;
using Unity.Collections;
using Unity.Netcode;

namespace Harvester.Race
{
    /// <summary>Yarış akışı. Sunucu ilerletir, herkes okur.</summary>
    public enum RacePhase : byte
    {
        Waiting,   // arabalar daha oluşturulmadı
        Countdown, // 3-2-1, girdi kapalı, harvester duruyor
        Racing,
        Finished,  // sonuç ekranı, sonra lobiye dönüş
    }

    public enum RacerStatus : byte
    {
        Racing,
        Finished,
        Eliminated, // harvester biçti
        Left,       // oyundan çıktı
    }

    /// <summary>Yarışın ne zaman biteceği. Kurallar değişebilir diye tek yerde.</summary>
    public enum RaceEndRule : byte
    {
        AllDone,      // herkes bitirince ya da elenince
        LastStanding, // yarışta tek kişi kalınca (bitiren ya da elenen herkes yarıştan çıkmış sayılır)
    }

    /// <summary>Bir yarışçının herkese yayınlanan durumu (RaceManager.Standings).</summary>
    public struct RacerState : INetworkSerializable, IEquatable<RacerState>
    {
        public ulong ClientId;
        public ulong CarObjectId;
        public FixedString32Bytes Name;
        public RacerStatus Status;
        /// <summary>1'den başlayan sıra.</summary>
        public byte Position;
        /// <summary>Şu anki tur (1..TurSayısı). A'dan B'ye pistte hep 1.</summary>
        public byte Lap;
        /// <summary>Başlangıç çizgisinden itibaren toplam ilerleme (m, turlar dahil). Sıralama buna göre.</summary>
        public float Progress;
        /// <summary>Bitiş ya da eleme anı (yarış başından itibaren sn).</summary>
        public float Time;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref CarObjectId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Lap);
            serializer.SerializeValue(ref Progress);
            serializer.SerializeValue(ref Time);
        }

        public bool Equals(RacerState o)
        {
            return ClientId == o.ClientId && CarObjectId == o.CarObjectId && Name.Equals(o.Name) && Status == o.Status
                && Position == o.Position && Lap == o.Lap && Progress == o.Progress && Time == o.Time;
        }
    }
}
