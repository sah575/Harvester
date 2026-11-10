using Harvester.Net;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

// Karakter prefab'larının ağ senkron ayarlarını tek yerden uygular.
// Menü: Harvester > Apply Network Sync Settings
public static class NetSyncBuilder
{
    static readonly string[] Robots = { "Assets/Prefabs/chars/robo1.prefab", "Assets/Prefabs/chars/robo2.prefab" };
    static readonly string[] Harvesters = { "Assets/Prefabs/modifiyeli/FRANKV2.prefab", "Assets/Prefabs/modifiyeli/FRANKV2.1.prefab" };

    [MenuItem("Harvester/Apply Network Sync Settings")]
    public static void Apply()
    {
        foreach (string path in Robots) Edit(path, false);
        foreach (string path in Harvesters) Edit(path, true);
        AssetDatabase.SaveAssets();

        // Prefab ayrı bir düzenleme sahnesinde kaydedilince NetworkObject'in GlobalObjectIdHash'i yanlış hesaplanıp
        // diske yazılıyor ("NetworkConfig mismatch"). Varlıkları yeniden serileştirmek doğru değeri geri yazar.
        var all = new System.Collections.Generic.List<string>(Robots);
        all.AddRange(Harvesters);
        AssetDatabase.ForceReserializeAssets(all);
        foreach (string path in all)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var no = prefab.GetComponent<Unity.Netcode.NetworkObject>();
            string disk = System.Array.Find(System.IO.File.ReadAllLines(path), l => l.Contains("GlobalObjectIdHash"));
            if (disk == null || !disk.Trim().EndsWith(no.PrefabIdHash.ToString()))
                Debug.LogError(path + ": diskteki hash (" + (disk ?? "yok").Trim() + ") editördekinden (" + no.PrefabIdHash + ") farklı");
        }
        Debug.Log("Ağ senkron ayarları uygulandı: " + (Robots.Length + Harvesters.Length) + " prefab.");
    }

    static void Edit(string path, bool harvester)
    {
        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var root = scope.prefabContentsRoot;
            var nt = root.GetComponent<NetworkTransform>();
            if (nt == null)
            {
                Debug.LogError(path + ": NetworkTransform yok");
                return;
            }

            // Kayıplı bağlantıda donup sıçramasın: güncellemeler güvenilmez gider, kaybolan paket beklenmez.
            nt.UseUnreliableDeltas = true;
            // Pozisyon tabana göre yarım hassasiyetli fark olarak gider (yarı boyut).
            nt.UseHalfFloatPrecision = true;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            nt.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
            nt.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
            nt.PositionLerpSmoothing = true;
            nt.RotationLerpSmoothing = true;
            nt.PositionMaxInterpolationTime = 0.1f;
            nt.RotationMaxInterpolationTime = 0.1f;
            // 1 cm altı kıpırtı gönderilmez.
            nt.PositionThreshold = 0.01f;

            if (harvester)
            {
                // Zeminde eğilir: tam dönüş, sıkıştırılmış quaternion olarak.
                nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = true;
                nt.UseQuaternionSynchronization = true;
                nt.UseQuaternionCompression = true;
                nt.RotAngleThreshold = 0.3f;
                // Yumuşatmayı RemoteExtrapolator'ın süzgeci yapıyor; NetworkTransform'unki sadece gecikme ekler.
                nt.PositionLerpSmoothing = false;
                nt.RotationLerpSmoothing = false;
                var ex = root.GetComponent<RemoteExtrapolator>();
                if (ex == null) ex = root.AddComponent<RemoteExtrapolator>();
                ex.PositionSmoothing = 0.1f;
                ex.Strength = 1f;
                ex.MaxLookahead = 0.6f;
                ex.MaxOffset = 12f;
                ex.MaxYawOffset = 45f;
                ex.SnapDistance = 8f;
            }
            else
            {
                // Robot sadece Y ekseninde döner (rb.freezeRotation).
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleY = true;
                nt.SyncRotAngleZ = false;
                nt.UseQuaternionSynchronization = false;
                nt.UseQuaternionCompression = false;
                nt.RotAngleThreshold = 0.5f;
            }
        }
    }
}
