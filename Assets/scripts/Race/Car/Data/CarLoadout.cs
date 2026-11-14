using System;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Oyuncunun araba seçimi: hangi araba, performans seviyeleri, parça varyantları ve renkler.
    /// Sabit boyutlu, yönetilmeyen (unmanaged) bir struct: kopyalaması ve ağda gönderilmesi çöp üretmez.
    /// Ağda 66 bayt: tanım (1) + sürüm/bayrak (1) + 8 geliştirme seviyesi (8) + 24 parça varyantı (24) + 8 renk (32).
    ///
    /// İndeksler tanımdaki sıralara göre: geliştirme = (int)CarUpgradeType, parça = CarDefinition.slots sırası,
    /// renk = CarDefinition.paints sırası. Rengin alfası 0 ise "tanımın varsayılan rengi" demek.
    /// Kalıcı kayıt (PlayerPrefs) sıraya değil adlara göre tutulur, bkz. CarLoadoutStore.
    /// </summary>
    [Serializable]
    public struct CarLoadout : INetworkSerializable, IEquatable<CarLoadout>
    {
        public const int MaxUpgrades = 8;
        public const int MaxSlots = 24;
        public const int MaxPaints = 8;

        /// <summary>CarCatalog'daki sıra.</summary>
        public byte definition;
        /// <summary>0 = boş (henüz seçim yok), 1 = geçerli.</summary>
        public byte flags;

        // Her bayt bir değer: upgrades 8 geliştirme, slots0..2 24 parça, paints her uint bir Color32 (r, g, b, a).
        private ulong upgrades;
        private ulong slots0, slots1, slots2;
        private uint paint0, paint1, paint2, paint3, paint4, paint5, paint6, paint7;

        public bool IsValid => (flags & 1) != 0;

        public static CarLoadout Create(int definitionIndex)
        {
            return new CarLoadout { definition = (byte)Mathf.Clamp(definitionIndex, 0, 255), flags = 1 };
        }

        // ---------------------------------------------------------------- Geliştirmeler

        public int GetUpgrade(int category)
        {
            if (category < 0 || category >= MaxUpgrades) return 0;
            return (int)((upgrades >> (category * 8)) & 0xFF);
        }

        public void SetUpgrade(int category, int level)
        {
            if (category < 0 || category >= MaxUpgrades) return;
            upgrades = SetByte(upgrades, category, level);
        }

        public int GetUpgrade(CarUpgradeType type) { return GetUpgrade((int)type); }
        public void SetUpgrade(CarUpgradeType type, int level) { SetUpgrade((int)type, level); }

        // ---------------------------------------------------------------- Parça varyantları

        public int GetSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return 0;
            ulong word = slot < 8 ? slots0 : slot < 16 ? slots1 : slots2;
            return (int)((word >> ((slot % 8) * 8)) & 0xFF);
        }

        public void SetSlot(int slot, int variant)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            int b = slot % 8;
            if (slot < 8) slots0 = SetByte(slots0, b, variant);
            else if (slot < 16) slots1 = SetByte(slots1, b, variant);
            else slots2 = SetByte(slots2, b, variant);
        }

        // ---------------------------------------------------------------- Renkler

        public Color32 GetPaint(int index)
        {
            uint v;
            switch (index)
            {
                case 0: v = paint0; break;
                case 1: v = paint1; break;
                case 2: v = paint2; break;
                case 3: v = paint3; break;
                case 4: v = paint4; break;
                case 5: v = paint5; break;
                case 6: v = paint6; break;
                case 7: v = paint7; break;
                default: return new Color32(0, 0, 0, 0);
            }
            return new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
        }

        public void SetPaint(int index, Color32 c)
        {
            uint v = c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);
            switch (index)
            {
                case 0: paint0 = v; break;
                case 1: paint1 = v; break;
                case 2: paint2 = v; break;
                case 3: paint3 = v; break;
                case 4: paint4 = v; break;
                case 5: paint5 = v; break;
                case 6: paint6 = v; break;
                case 7: paint7 = v; break;
            }
        }

        // ---------------------------------------------------------------- Ağ

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref definition);
            serializer.SerializeValue(ref flags);
            serializer.SerializeValue(ref upgrades);
            serializer.SerializeValue(ref slots0);
            serializer.SerializeValue(ref slots1);
            serializer.SerializeValue(ref slots2);
            serializer.SerializeValue(ref paint0);
            serializer.SerializeValue(ref paint1);
            serializer.SerializeValue(ref paint2);
            serializer.SerializeValue(ref paint3);
            serializer.SerializeValue(ref paint4);
            serializer.SerializeValue(ref paint5);
            serializer.SerializeValue(ref paint6);
            serializer.SerializeValue(ref paint7);
        }

        public bool Equals(CarLoadout o)
        {
            return definition == o.definition && flags == o.flags && upgrades == o.upgrades
                && slots0 == o.slots0 && slots1 == o.slots1 && slots2 == o.slots2
                && paint0 == o.paint0 && paint1 == o.paint1 && paint2 == o.paint2 && paint3 == o.paint3
                && paint4 == o.paint4 && paint5 == o.paint5 && paint6 == o.paint6 && paint7 == o.paint7;
        }

        public override bool Equals(object obj) { return obj is CarLoadout && Equals((CarLoadout)obj); }

        public override int GetHashCode()
        {
            unchecked
            {
                ulong h = upgrades ^ (slots0 * 31) ^ (slots1 * 131) ^ (slots2 * 1031) ^ ((ulong)definition << 56)
                    ^ paint0 ^ ((ulong)paint1 << 32) ^ paint2 ^ ((ulong)paint3 << 32) ^ paint4 ^ ((ulong)paint5 << 32);
                return (int)h ^ (int)(h >> 32);
            }
        }

        public override string ToString()
        {
            return "Araba " + definition + (IsValid ? "" : " (boş)") + ", geliştirme " + upgrades.ToString("X16")
                + ", parça " + slots0.ToString("X16") + slots1.ToString("X16") + slots2.ToString("X16");
        }

        private static ulong SetByte(ulong word, int index, int value)
        {
            int shift = index * 8;
            return (word & ~(0xFFUL << shift)) | ((ulong)(byte)Mathf.Clamp(value, 0, 255) << shift);
        }
    }
}
