using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Harvester.Race;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// FBX eksen/birim dönüşümünü kalibrasyon mesh'i ile tersine çevirir; mevcut pivotlar aynen kalır.
public static class HarvesterLodBuilder
{
    const string Folder="Assets/Models/Harvester/LOD";
    const string Fbx=Folder+"/Harvester_LOD.fbx";
    [Serializable] class Indices { public int[] values; }
    [Serializable] class SourceMesh { public string name; public Vector3[] vertices; public Vector2[] uv; public Indices[] submeshes; }
    [Serializable] class Source { public SourceMesh[] meshes; }
    static Mesh[] Originals()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Harvester/HarvesterGorsel.prefab");
        return root.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh).Where(x=>x!=null).Distinct().OrderBy(x=>x.name,StringComparer.Ordinal).ToArray();
    }
    [MenuItem("Harvester/LOD/Harvester Kaynağını Dışa Aktar")]
    public static void ExportSource()
    {
        var originals=Originals();var data=new Source{meshes=new SourceMesh[originals.Length]};
        for(int i=0;i<originals.Length;i++)
        {
            var m=originals[i];var sub=new Indices[m.subMeshCount];
            for(int s=0;s<sub.Length;s++)sub[s]=new Indices{values=m.GetTriangles(s)};
            data.meshes[i]=new SourceMesh{name=m.name,vertices=m.vertices,uv=m.uv,submeshes=sub};
        }
        File.WriteAllText("/tmp/harvester-lod-source.json",JsonUtility.ToJson(data));
        Debug.Log("Harvester LOD kaynağı: /tmp/harvester-lod-source.json; mesh="+originals.Length);
    }
    [MenuItem("Harvester/LOD/Harvester LOD Meshlerini Üret")]
    public static void BuildAssets()
    {
        var importer=AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if(importer==null)throw new InvalidOperationException("Önce BlenderSources/harvester/uretim_lod.py ile FBX üret.");
        importer.isReadable=true;importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        var imported=AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var filters=imported.GetComponentsInChildren<MeshFilter>(true);
        var calibration=filters.First(x=>x.name=="CoordinateCalibration").sharedMesh;
        var axes=calibration.vertices.Where(v=>v.sqrMagnitude>1e-10f).Distinct().OrderBy(v=>v.sqrMagnitude).ToArray();
        if(axes.Length!=3)throw new InvalidOperationException("FBX koordinat kalibrasyonu geçersiz.");
        var matrix=Matrix4x4.identity;
        for(int i=0;i<3;i++){Vector3 v=axes[i]/(i+1);matrix.SetColumn(i,new Vector4(v.x,v.y,v.z,0));}
        var inverse=matrix.inverse;var normalMatrix=matrix.transpose;
        var originals=Originals();var counts=new int[2];
        for(int level=0;level<2;level++)for(int i=0;i<originals.Length;i++)
        {
            string name="L"+level+"_"+i.ToString("000");
            var filter=filters.First(x=>x.name==name);var source=filter.sharedMesh;
            var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Harvester_"+name;
            mesh.vertices=source.vertices.Select(v=>inverse.MultiplyPoint3x4(v)).ToArray();
            mesh.normals=source.normals.Select(v=>normalMatrix.MultiplyVector(v).normalized).ToArray();
            var submeshes=new List<int>[originals[i].subMeshCount];for(int s=0;s<submeshes.Length;s++)submeshes[s]=new List<int>();
            var materials=filter.GetComponent<MeshRenderer>().sharedMaterials;
            for(int s=0;s<source.subMeshCount;s++)
            {
                int slot=int.Parse(materials[s].name.Substring("Yuva_".Length));var indices=source.GetTriangles(s);
                if(inverse.determinant<0)for(int t=0;t<indices.Length;t+=3){int a=indices[t];indices[t]=indices[t+1];indices[t+1]=a;}
                submeshes[slot].AddRange(indices);counts[level]+=indices.Length/3;
            }
            mesh.subMeshCount=submeshes.Length;for(int s=0;s<submeshes.Length;s++)mesh.SetTriangles(submeshes[s],s);
            mesh.RecalculateBounds();
            var old=originals[i].bounds;
            if((mesh.bounds.center-old.center).magnitude>Mathf.Max(.05f,old.size.magnitude*.1f)||
               (mesh.bounds.size-old.size).magnitude>Mathf.Max(.1f,old.size.magnitude*.2f))
                throw new InvalidOperationException("LOD sınırları kaynakla uyuşmuyor: "+name+" "+mesh.bounds+" / "+old);
            mesh.bounds=old;mesh.RecalculateTangents();
            // Kabin, cam ve ayna gibi zaten küçük parçaların yüzeylerini/normallerini koru.
            long originalTriangles=0;for(int s=0;s<originals[i].subMeshCount;s++)originalTriangles+=(long)originals[i].GetIndexCount(s)/3;
            if(originalTriangles<=500)
            {UnityEngine.Object.DestroyImmediate(mesh);mesh=UnityEngine.Object.Instantiate(originals[i]);mesh.name="Harvester_"+name;}
            string path=Folder+"/Harvester_"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null)AssetDatabase.CreateAsset(mesh,path);
            else{EditorUtility.CopySerialized(mesh,existing);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(mesh);}
        }
        AssetDatabase.SaveAssets();Debug.Log("LOD benzersiz mesh üçgenleri: "+counts[0]+" / "+counts[1]);
    }
    public static void Attach(GameObject root,Transform model,HarvesterAI ai)
    {
        if(!File.Exists(Fbx))return;
        var originals=Originals();var far=UnityEngine.Object.Instantiate(model.gameObject,root.transform,false);far.name="Model_LOD1";
        var sourceRenderers=model.GetComponentsInChildren<MeshRenderer>(true);
        var farRenderers=far.GetComponentsInChildren<MeshRenderer>(true);
        var models=new[]{model,far.transform};var totals=new int[2];
        for(int level=0;level<2;level++)foreach(var filter in models[level].GetComponentsInChildren<MeshFilter>(true))
        {
            int index=Array.IndexOf(originals,filter.sharedMesh);
            if(index<0)throw new InvalidOperationException("LOD kaynak mesh eşleşmedi: "+filter.name);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Harvester_L"+level+"_"+index.ToString("000")+".asset");
            if(mesh==null)throw new InvalidOperationException("Önce LOD Meshlerini Üret menüsünü çalıştır.");
            filter.sharedMesh=mesh;for(int s=0;s<mesh.subMeshCount;s++)totals[level]+=(int)mesh.GetIndexCount(s)/3;
        }
        var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.CrossFade;group.animateCrossFading=true;
        group.SetLODs(new[]{new LOD(.10f,sourceRenderers){fadeTransitionWidth=.15f},new LOD(.002f,farRenderers)});group.RecalculateBounds();
        var sync=root.AddComponent<HarvesterVisualLod>();var so=new SerializedObject(ai);var transforms=new List<Transform>();
        transforms.Add((Transform)so.FindProperty("headerVisual").objectReferenceValue);transforms.Add((Transform)so.FindProperty("reel").objectReferenceValue);
        var wheels=so.FindProperty("wheels");for(int i=0;i<wheels.arraySize;i++)transforms.Add((Transform)wheels.GetArrayElementAtIndex(i).objectReferenceValue);
        sync.sources=transforms.Where(t=>t!=null).ToArray();
        sync.targets=sync.sources.Select(t=>far.transform.Find(AnimationUtility.CalculateTransformPath(t,model))).ToArray();
        if(sync.targets.Any(t=>t==null))throw new InvalidOperationException("LOD hareketli parça eşleşmedi.");
        Debug.Log("Harvester görsel LOD üçgenleri: "+totals[0]+" / "+totals[1]);
    }
}
