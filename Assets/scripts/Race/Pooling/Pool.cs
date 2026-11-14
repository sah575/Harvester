using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Harvester.Race
{
    // Yalnız yerel efektler için, ilk kullanıma kadar nesne oluşturmaz. Enkaz sistemine bağlanmadı.
    public static class Pool
    {
        internal sealed class Bucket
        {
            internal GameObject prefab;
            internal Transform storage;
            internal bool disposed;
            internal readonly Stack<PooledObject> idle=new Stack<PooledObject>();
        }
        static readonly Dictionary<GameObject,Bucket> buckets=new Dictionary<GameObject,Bucket>();
        static Transform root;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics(){buckets.Clear();root=null;}
        static Bucket Get(GameObject prefab)
        {
            if(prefab==null)throw new ArgumentNullException(nameof(prefab));
            Bucket bucket;if(buckets.TryGetValue(prefab,out bucket))return bucket;
            if(root==null){var go=new GameObject("Yerel efekt havuzları");UnityEngine.Object.DontDestroyOnLoad(go);root=go.transform;}
            var storage=new GameObject(prefab.name+" (boş havuz)");storage.transform.SetParent(root,false);storage.SetActive(false);
            bucket=new Bucket{prefab=prefab,storage=storage.transform};buckets.Add(prefab,bucket);return bucket;
        }
        static PooledObject Create(Bucket bucket)
        {
            // Etkisiz ebeveyn, ön ısıtmada OnEnable/efekt tetiklenmesini önler.
            var go=UnityEngine.Object.Instantiate(bucket.prefab,bucket.storage,false);go.SetActive(false);
            var pooled=go.GetComponent<PooledObject>();if(pooled==null)pooled=go.AddComponent<PooledObject>();
            pooled.bucket=bucket;pooled.IsInPool=true;pooled.Cache();return pooled;
        }
        public static void Prewarm(GameObject prefab,int count)
        {var bucket=Get(prefab);while(bucket.idle.Count<Mathf.Max(0,count))bucket.idle.Push(Create(bucket));}
        public static GameObject Spawn(GameObject prefab,Vector3 position,Quaternion rotation,Transform parent=null)
        {
            var bucket=Get(prefab);PooledObject pooled=null;
            while(bucket.idle.Count>0&&pooled==null)pooled=bucket.idle.Pop();
            if(pooled==null)pooled=Create(bucket);
            pooled.IsInPool=false;pooled.transform.SetParent(parent,false);
            if(parent==null)SceneManager.MoveGameObjectToScene(pooled.gameObject,SceneManager.GetActiveScene());
            pooled.transform.localScale=pooled.initialScale;pooled.transform.SetPositionAndRotation(position,rotation);
            pooled.gameObject.SetActive(true);
            foreach(var callback in pooled.callbacks)callback.OnPoolSpawn();
            return pooled.gameObject;
        }
        public static void Release(GameObject instance)
        {
            if(instance==null)return;PooledObject pooled;
            if(!instance.TryGetComponent<PooledObject>(out pooled)||pooled.bucket==null)
                throw new ArgumentException("Nesne Pool.Spawn ile oluşturulmamış.",nameof(instance));
            if(pooled.IsInPool)return;pooled.IsInPool=true;
            try{foreach(var callback in pooled.callbacks)callback.OnPoolRelease();}
            finally
            {
                pooled.ResetForRelease();instance.SetActive(false);
                var bucket=pooled.bucket;
                if(bucket.disposed||bucket.storage==null)UnityEngine.Object.Destroy(instance);
                else{pooled.transform.SetParent(bucket.storage,false);bucket.idle.Push(pooled);}
            }
        }
        public static int InactiveCount(GameObject prefab)
        {Bucket bucket;return prefab!=null&&buckets.TryGetValue(prefab,out bucket)?bucket.idle.Count:0;}
        public static void Clear()
        {
            foreach(var bucket in buckets.Values){bucket.disposed=true;if(bucket.storage!=null)UnityEngine.Object.Destroy(bucket.storage.gameObject);bucket.idle.Clear();}
            buckets.Clear();if(root!=null)UnityEngine.Object.Destroy(root.gameObject);root=null;
        }
    }
}
