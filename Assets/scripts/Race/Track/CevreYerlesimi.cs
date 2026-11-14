using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harvester.Race
{
    /// <summary>
    /// Haritanın çevre nesneleri (ağaç, çimen, kaya, tabela) için veri: model başına LOD mesh'leri ve malzemeler,
    /// yerleşimler 64 m'lik hücrelere bölünmüş dönüşüm matrisleri olarak. Kurucu (TrackBuilder) Antigravity'nin
    /// Dag_Yerlesim.json'ından üretir; sahnede CevreCizici çizer.
    /// </summary>
    public class CevreYerlesimi : ScriptableObject
    {
        [Serializable]
        public class Model
        {
            public string name;
            public Mesh lod0, lod1;
            public Material[] materials;
            [Tooltip("Modelin kendi içindeki dönüşüm (FBX eksen çevrimi); yerleşim matrisiyle çarpılır")]
            public Matrix4x4 meshOffset = Matrix4x4.identity;
            public bool castShadows = true;
            [Tooltip("Bu modelin en fazla çizildiği uzaklık (m). Çimen kısa, ağaç uzun.")]
            public float drawDistance = 220f;
            [Tooltip("LOD0'dan LOD1'e geçiş uzaklığı (m)")]
            public float lodDistance = 60f;
        }

        [Serializable]
        public class Cell
        {
            public int model;
            public Bounds bounds;
            public Matrix4x4[] matrices;
        }

        public float cellSize = 64f;
        public List<Model> models = new List<Model>();
        public List<Cell> cells = new List<Cell>();

        public int InstanceCount
        {
            get { int n = 0; foreach (var c in cells) n += c.matrices.Length; return n; }
        }
    }
}
