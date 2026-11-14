using System;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Arabanın fizik değerleri. RaceCar'daki ayar alanlarıyla birebir aynı adlarda ve anlamda (artı dayanıklılık).
    /// Bir arabanın tanımında (CarDefinition.baseStats) durur; performans geliştirmeleri bunun üstüne uygulanır
    /// (CarDefinition.ComputeStats). Varsayılanlar, Kenney arabasında ayarlanıp test edilen değerler.
    /// Tekerlek konumu, yarıçapı ve dingil mesafesi burada değil: onlar modelden ölçülür (CarGeometry).
    /// </summary>
    [Serializable]
    public struct CarStats
    {
        [Header("Motor ve fren")]
        [Tooltip("Son hız (m/sn). 40 m/sn = 144 km/sa")]
        public float topSpeed;
        [Tooltip("Kalkıştaki en yüksek ivme (m/sn²)")]
        public float maxAcceleration;
        [Tooltip("İtiş eğrisi: yatay eksen hız/son hız (0–1), dikey eksen ivme oranı")]
        public AnimationCurve accelerationCurve;
        [Tooltip("Fren yavaşlaması (m/sn²)")]
        public float brakeDeceleration;
        [Tooltip("Geri vites son hızı (m/sn)")]
        public float reverseTopSpeed;
        [Tooltip("Geri vites ivmesi (m/sn²)")]
        public float reverseAcceleration;
        [Tooltip("Gaz bırakılınca yavaşlama (m/sn²)")]
        public float coastDeceleration;
        [Tooltip("El freninin kendi yavaşlatması (m/sn²)")]
        public float handbrakeDeceleration;
        [Tooltip("Hızın karesiyle artan bastırma kuvveti katsayısı (N/(m/sn)²)")]
        public float downforce;

        [Header("Direksiyon")]
        [Tooltip("Düşük hızda en büyük direksiyon açısı (derece)")]
        public float maxSteerAngle;
        [Tooltip("Son hızda en büyük direksiyon açısı (derece)")]
        public float highSpeedSteerAngle;
        [Tooltip("Direksiyon açısının en küçüğe indiği hız (m/sn)")]
        public float steerFalloffSpeed;
        [Tooltip("Direksiyonun dönme hızı (derece/sn)")]
        public float steerSpeed;
        [Tooltip("Dönüş yardımı (1/sn)")]
        public float steerAssist;
        [Tooltip("Dönüş yardımının istediği en büyük yanal ivme (m/sn²)")]
        public float maxCornerAccel;

        [Header("Lastik tutuşu")]
        [Tooltip("Ön lastiklerin yanal tutuşu (1/sn)")]
        public float frontGrip;
        [Tooltip("Arka lastiklerin yanal tutuşu (1/sn)")]
        public float rearGrip;
        [Tooltip("Sürtünme katsayısı: yanal kuvvet tekerlek yükünün en fazla bu katı")]
        public float tireFriction;
        [Tooltip("El freninde arka tutuşun çarpanı (drift)")]
        [Range(0f, 1f)] public float handbrakeRearGrip;
        [Tooltip("El freni çekiliyken dönüş yardımının hedef sapma hızı çarpanı")]
        public float driftYawBoost;
        [Tooltip("Yanal kuvvetin uygulandığı yükseklik: 0 = lastik teması, 1 = ağırlık merkezi")]
        [Range(0f, 1f)] public float gripForceHeight;

        [Header("Gövde ve süspansiyon")]
        [Tooltip("Kütle (kg)")]
        public float mass;
        [Tooltip("Ağırlık merkezinin gövde kökünden yüksekliği (m). Düşük tutulursa devrilmez.")]
        public float centerOfMassHeight;
        [Tooltip("Tekerleğin dinlenme konumundan yukarı ne kadar sıkışabileceği (m)")]
        public float suspensionUp;
        [Tooltip("Dinlenme konumundan aşağı sarkma (m)")]
        public float suspensionDroop;
        [Tooltip("Yay sertliği (N/m, tekerlek başına). Yaklaşık kütle*9.81/4/sarkma.")]
        public float springStrength;
        [Tooltip("Sönüm (N·sn/m, tekerlek başına)")]
        public float damperStrength;
        [Tooltip("Viraj demiri (N/m)")]
        public float antiRoll;
        [Tooltip("Havada gövdeyi düz tutan dönme ivmesi (1/sn²)")]
        public float airUpright;
        [Tooltip("Havada dönmenin sönümü (1/sn)")]
        public float airAngularDamping;

        [Header("Dayanıklılık")]
        [Tooltip("Dayanıklılık puanı (0–100): çarpmaya ve harvester itmesine direnç. 60 = referans (itme olduğu gibi), " +
                 "her 10 puan itmeyi %10 azaltır/artırır (bkz. HitScale). İleride hasar da buna bağlanacak.")]
        public float durability;

        /// <summary>Referans dayanıklılık: bu değerde çarpmalar olduğu gibi uygulanır.</summary>
        public const float ReferenceDurability = 60f;

        /// <summary>Çarpma hız değişiminin çarpanı: 60 → 1, 80 → 0.8, 40 → 1.2 (0.5–1.5 arası). 0 (eski kayıt) → 1.</summary>
        public static float HitScale(float durability)
        {
            if (durability <= 0f) return 1f;
            return Mathf.Clamp(1f - (durability - ReferenceDurability) / 100f, 0.5f, 1.5f);
        }

        /// <summary>Kenney arabasında ayarlanıp test edilmiş değerler (RaceCar.prefab, 13 Kasım).</summary>
        public static CarStats Default
        {
            get
            {
                return new CarStats
                {
                    topSpeed = 40f,
                    maxAcceleration = 12f,
                    accelerationCurve = DefaultCurve(),
                    brakeDeceleration = 22f,
                    reverseTopSpeed = 12f,
                    reverseAcceleration = 8f,
                    coastDeceleration = 2.5f,
                    handbrakeDeceleration = 6f,
                    downforce = 2.5f,
                    maxSteerAngle = 34f,
                    highSpeedSteerAngle = 9f,
                    steerFalloffSpeed = 35f,
                    steerSpeed = 160f,
                    steerAssist = 5f,
                    maxCornerAccel = 15f,
                    frontGrip = 28f,
                    rearGrip = 34f,
                    tireFriction = 1.5f,
                    handbrakeRearGrip = 0.55f,
                    driftYawBoost = 1.25f,
                    gripForceHeight = 0.75f,
                    mass = 1200f,
                    centerOfMassHeight = 0.39375f,
                    suspensionUp = 0.22f,
                    suspensionDroop = 0.12f,
                    springStrength = 24500f,
                    damperStrength = 2700f,
                    antiRoll = 9000f,
                    airUpright = 4f,
                    airAngularDamping = 1.5f,
                    durability = ReferenceDurability,
                };
            }
        }

        public static AnimationCurve DefaultCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.85f), new Keyframe(0.85f, 0.45f), new Keyframe(1f, 0f));
        }

        /// <summary>Geliştirmelerin değiştirebildiği tek bir değere erişir (eğri hariç).</summary>
        public float Get(CarStat stat)
        {
            switch (stat)
            {
                case CarStat.SonHiz: return topSpeed;
                case CarStat.Ivme: return maxAcceleration;
                case CarStat.Fren: return brakeDeceleration;
                case CarStat.GeriSonHiz: return reverseTopSpeed;
                case CarStat.GeriIvme: return reverseAcceleration;
                case CarStat.BosVitesYavaslama: return coastDeceleration;
                case CarStat.ElFreniYavaslama: return handbrakeDeceleration;
                case CarStat.BastirmaKuvveti: return downforce;
                case CarStat.DireksiyonAcisi: return maxSteerAngle;
                case CarStat.YuksekHizDireksiyonAcisi: return highSpeedSteerAngle;
                case CarStat.DireksiyonAzalmaHizi: return steerFalloffSpeed;
                case CarStat.DireksiyonHizi: return steerSpeed;
                case CarStat.DonusYardimi: return steerAssist;
                case CarStat.VirajIvmeSiniri: return maxCornerAccel;
                case CarStat.OnTutus: return frontGrip;
                case CarStat.ArkaTutus: return rearGrip;
                case CarStat.LastikSurtunmesi: return tireFriction;
                case CarStat.ElFreniArkaTutus: return handbrakeRearGrip;
                case CarStat.DriftSapma: return driftYawBoost;
                case CarStat.TutusKuvvetYuksekligi: return gripForceHeight;
                case CarStat.Kutle: return mass;
                case CarStat.AgirlikMerkeziYuksekligi: return centerOfMassHeight;
                case CarStat.SuspansiyonYukari: return suspensionUp;
                case CarStat.SuspansiyonSarkma: return suspensionDroop;
                case CarStat.Yay: return springStrength;
                case CarStat.Sonum: return damperStrength;
                case CarStat.VirajDemiri: return antiRoll;
                case CarStat.HavadaDuzelme: return airUpright;
                case CarStat.HavadaSonum: return airAngularDamping;
                case CarStat.Dayaniklilik: return durability;
            }
            return 0f;
        }

        public void Set(CarStat stat, float value)
        {
            switch (stat)
            {
                case CarStat.SonHiz: topSpeed = value; break;
                case CarStat.Ivme: maxAcceleration = value; break;
                case CarStat.Fren: brakeDeceleration = value; break;
                case CarStat.GeriSonHiz: reverseTopSpeed = value; break;
                case CarStat.GeriIvme: reverseAcceleration = value; break;
                case CarStat.BosVitesYavaslama: coastDeceleration = value; break;
                case CarStat.ElFreniYavaslama: handbrakeDeceleration = value; break;
                case CarStat.BastirmaKuvveti: downforce = value; break;
                case CarStat.DireksiyonAcisi: maxSteerAngle = value; break;
                case CarStat.YuksekHizDireksiyonAcisi: highSpeedSteerAngle = value; break;
                case CarStat.DireksiyonAzalmaHizi: steerFalloffSpeed = value; break;
                case CarStat.DireksiyonHizi: steerSpeed = value; break;
                case CarStat.DonusYardimi: steerAssist = value; break;
                case CarStat.VirajIvmeSiniri: maxCornerAccel = value; break;
                case CarStat.OnTutus: frontGrip = value; break;
                case CarStat.ArkaTutus: rearGrip = value; break;
                case CarStat.LastikSurtunmesi: tireFriction = value; break;
                case CarStat.ElFreniArkaTutus: handbrakeRearGrip = Mathf.Clamp01(value); break;
                case CarStat.DriftSapma: driftYawBoost = value; break;
                case CarStat.TutusKuvvetYuksekligi: gripForceHeight = Mathf.Clamp01(value); break;
                case CarStat.Kutle: mass = Mathf.Max(100f, value); break;
                case CarStat.AgirlikMerkeziYuksekligi: centerOfMassHeight = value; break;
                case CarStat.SuspansiyonYukari: suspensionUp = Mathf.Max(0.01f, value); break;
                case CarStat.SuspansiyonSarkma: suspensionDroop = Mathf.Max(0.01f, value); break;
                case CarStat.Yay: springStrength = value; break;
                case CarStat.Sonum: damperStrength = value; break;
                case CarStat.VirajDemiri: antiRoll = value; break;
                case CarStat.HavadaDuzelme: airUpright = value; break;
                case CarStat.HavadaSonum: airAngularDamping = value; break;
                case CarStat.Dayaniklilik: durability = Mathf.Clamp(value, 1f, 100f); break;
            }
        }
    }

    /// <summary>Geliştirmelerin değiştirebildiği değerler. Sıra kayıtlı asset'lerde sayı olarak durur: sona ekle, araya ekleme.</summary>
    public enum CarStat
    {
        SonHiz, Ivme, Fren, GeriSonHiz, GeriIvme, BosVitesYavaslama, ElFreniYavaslama, BastirmaKuvveti,
        DireksiyonAcisi, YuksekHizDireksiyonAcisi, DireksiyonAzalmaHizi, DireksiyonHizi, DonusYardimi, VirajIvmeSiniri,
        OnTutus, ArkaTutus, LastikSurtunmesi, ElFreniArkaTutus, DriftSapma, TutusKuvvetYuksekligi,
        Kutle, AgirlikMerkeziYuksekligi, SuspansiyonYukari, SuspansiyonSarkma, Yay, Sonum, VirajDemiri,
        HavadaDuzelme, HavadaSonum,
        Dayaniklilik,
    }
}
