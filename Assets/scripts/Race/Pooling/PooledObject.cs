using UnityEngine;

namespace Harvester.Race
{
    public sealed class PooledObject : MonoBehaviour
    {
        internal Pool.Bucket bucket;
        internal IPoolable[] callbacks;
        internal Rigidbody[] bodies;
        internal ParticleSystem[] particles;
        internal Vector3 initialScale;
        public bool IsInPool { get; internal set; }
        float releaseAt;
        internal void Cache()
        {
            var list=new System.Collections.Generic.List<IPoolable>();
            foreach(var component in GetComponentsInChildren<MonoBehaviour>(true))if(component is IPoolable)list.Add((IPoolable)component);
            callbacks=list.ToArray();bodies=GetComponentsInChildren<Rigidbody>(true);particles=GetComponentsInChildren<ParticleSystem>(true);
            initialScale=transform.localScale;enabled=false;
        }
        // Oyun zamanına bağlı ömür; duraklatma efekt ömrünü de durdurur.
        public void ReleaseAfter(float seconds){releaseAt=Time.time+Mathf.Max(0,seconds);enabled=true;}
        internal void ResetForRelease()
        {
            enabled=false;
            foreach(var particle in particles)if(particle!=null)particle.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach(var body in bodies)if(body!=null&&!body.isKinematic)
            {body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.Sleep();}
        }
        void Update(){if(Time.time>=releaseAt)Pool.Release(gameObject);}
    }
}
