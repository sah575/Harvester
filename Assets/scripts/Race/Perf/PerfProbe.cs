using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Harvester.Race
{
    // Yalnız açık komut satırı isteğiyle çalışır. Yarış davranışını veya kaliteyi değiştirmez.
    [DefaultExecutionOrder(3000)]
    public sealed class PerfProbe : MonoBehaviour
    {
        struct Sample
        {
            public double seconds,wall,cpu,main,render,gpu,wait;
            public long draws,passes,batches,triangles,shadows,gc;
            public int alive;
            public float scale;
        }
        [Serializable] public class Metric
        {public string name;public int validSamples;public double p50,p95,p99;}
        [Serializable] public class Summary
        {
            public string label,gpu,api,quality,unity,csv,persistentDataPath;
            public int width,height,frames,minAlive,maxAlive,fourAliveFrames;
            public double capturedSeconds,wallSeconds;
            public float renderScale;
            public bool mirrorRequested,frameTimingAvailable;
            public List<Metric> metrics=new List<Metric>();
        }
        readonly FrameTiming[] timing=new FrameTiming[1];
        List<Sample> samples;
        ProfilerRecorder draws,passes,batches,triangles,shadows,gc,mainThread,renderThread;
        double duration,captured,start,progress;
        float warmed;
        RaceManager lastManager;
        string label,folder;
        bool done,oldMirror;
        StackTraceLogType oldLog;
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args=Environment.GetCommandLineArgs();double seconds;
            if(!double.TryParse(Arg(args,"-perfProbe"),NumberStyles.Float,Inv,out seconds)||seconds<=0)return;
            var go=new GameObject("Performans ölçümü");DontDestroyOnLoad(go);
            var probe=go.AddComponent<PerfProbe>();probe.Begin(Math.Min(seconds,600),args);
#endif
        }
        static string Arg(string[] args,string key)
        {int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        void Begin(double seconds,string[] args)
        {
            duration=seconds;label=Arg(args,"-perfLabel")??"olcum";
            foreach(char c in Path.GetInvalidFileNameChars())label=label.Replace(c,'_');
            folder=Arg(args,"-perfOutput")??Path.Combine(Application.persistentDataPath,"perf");Directory.CreateDirectory(folder);
            samples=new List<Sample>(Mathf.Clamp((int)(seconds*1000),10000,600000));
            draws=Counter(ProfilerCategory.Render,"Draw Calls Count");passes=Counter(ProfilerCategory.Render,"SetPass Calls Count");
            batches=Counter(ProfilerCategory.Render,"Batches Count");triangles=Counter(ProfilerCategory.Render,"Triangles Count");
            shadows=Counter(ProfilerCategory.Render,"Shadow Casters Count");gc=Counter(ProfilerCategory.Memory,"GC Allocated In Frame");
            mainThread=Counter(ProfilerCategory.Internal,"Main Thread");renderThread=Counter(ProfilerCategory.Internal,"Render Thread");
            oldMirror=RearViewMirror.UserEnabled;RearViewMirror.UserEnabled=true;Application.runInBackground=true;
            oldLog=Application.GetStackTraceLogType(LogType.Log);Application.SetStackTraceLogType(LogType.Log,StackTraceLogType.None);
            start=Time.realtimeSinceStartupAsDouble;
            Debug.Log("[Perf] BAŞLADI etiket="+label+" GPU="+SystemInfo.graphicsDeviceName+" API="+SystemInfo.graphicsDeviceType+" hedef="+duration+" sn");
        }
        static ProfilerRecorder Counter(ProfilerCategory category,string name)
        {var r=ProfilerRecorder.StartNew(category,name,1);if(!r.Valid)Debug.LogWarning("[Perf] Sayaç desteklenmiyor: "+name);return r;}
        static long Value(ProfilerRecorder r){return r.Valid?r.LastValue:-1;}
        void LateUpdate()
        {
            if(done||samples==null)return;
            FrameTimingManager.CaptureFrameTimings();
            var manager=RaceManager.Instance;
            if(manager!=lastManager){lastManager=manager;warmed=0;}
            if(manager==null||manager.Phase!=RacePhase.Racing)return;
            warmed+=Time.unscaledDeltaTime;
            var mirror=RearViewMirror.Instance;
            if(warmed<2||RaceCar.All.Count!=4||RaceCar.Local==null||RaceCar.Local.IsEliminated||mirror==null||!mirror.IsShowing)return;
            int alive=0;foreach(var car in RaceCar.All)if(car!=null&&!car.IsEliminated)alive++;
            bool hasTiming=FrameTimingManager.GetLatestTimings(1,timing)>0;var t=timing[0];
            double main=hasTiming&&t.cpuMainThreadFrameTime>0?t.cpuMainThreadFrameTime:Value(mainThread)/1e6;
            double render=hasTiming&&t.cpuRenderThreadFrameTime>0?t.cpuRenderThreadFrameTime:Value(renderThread)/1e6;
            var asset=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            captured+=Time.unscaledDeltaTime;
            samples.Add(new Sample{seconds=captured,wall=Time.unscaledDeltaTime*1000,cpu=hasTiming&&t.cpuFrameTime>0?t.cpuFrameTime:-1,
                main=main>0?main:-1,render=render>0?render:-1,gpu=hasTiming&&t.gpuFrameTime>0?t.gpuFrameTime:-1,wait=hasTiming?t.cpuMainThreadPresentWaitTime:-1,
                draws=Value(draws),passes=Value(passes),batches=Value(batches),triangles=Value(triangles),shadows=Value(shadows),gc=Value(gc),alive=alive,scale=asset!=null?asset.renderScale:-1});
            if(captured>=progress+10){progress=captured;Debug.Log("[Perf] örneklenen="+captured.ToString("F1",Inv)+"/"+duration+" sn; yaşayan="+alive);}
            if(captured>=duration)Finish();
        }
        void Finish()
        {
            done=true;
            string path=Path.Combine(folder,label+".csv");
            using(var writer=new StreamWriter(path,false,Encoding.UTF8))
            {
                writer.WriteLine("seconds,wall_ms,cpu_ms,cpu_main_ms,cpu_render_ms,gpu_ms,present_wait_ms,draw_calls,setpass,batches,triangles,shadow_casters,gc_bytes,alive_cars,render_scale");
                foreach(var s in samples)writer.WriteLine(string.Join(",",new[]{F(s.seconds),F(s.wall),F(s.cpu),F(s.main),F(s.render),F(s.gpu),F(s.wait),F(s.draws),F(s.passes),F(s.batches),F(s.triangles),F(s.shadows),F(s.gc),F(s.alive),F(s.scale)}));
            }
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var summary=new Summary{label=label,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),quality=QualitySettings.names[QualitySettings.GetQualityLevel()],unity=Application.unityVersion,
                csv=path,persistentDataPath=Application.persistentDataPath,width=Screen.width,height=Screen.height,frames=samples.Count,capturedSeconds=captured,wallSeconds=Time.realtimeSinceStartupAsDouble-start,
                renderScale=pipeline!=null?pipeline.renderScale:-1,mirrorRequested=RearViewMirror.UserEnabled,frameTimingAvailable=FrameTimingManager.IsFeatureEnabled(),minAlive=4};
            foreach(var s in samples){summary.minAlive=Math.Min(summary.minAlive,s.alive);summary.maxAlive=Math.Max(summary.maxAlive,s.alive);if(s.alive==4)summary.fourAliveFrames++;}
            Add(summary,"wall_ms",s=>s.wall);Add(summary,"cpu_ms",s=>s.cpu);Add(summary,"cpu_main_ms",s=>s.main);Add(summary,"cpu_render_ms",s=>s.render);Add(summary,"gpu_ms",s=>s.gpu);Add(summary,"present_wait_ms",s=>s.wait);
            Add(summary,"draw_calls",s=>s.draws);Add(summary,"setpass",s=>s.passes);Add(summary,"batches",s=>s.batches);Add(summary,"triangles",s=>s.triangles);Add(summary,"shadow_casters",s=>s.shadows);Add(summary,"gc_bytes",s=>s.gc);
            File.WriteAllText(Path.Combine(folder,label+".json"),JsonUtility.ToJson(summary,true));
            foreach(var m in summary.metrics)Debug.Log("[Perf] "+m.name+" n="+m.validSamples+" p50="+F(m.p50)+" p95="+F(m.p95)+" p99="+F(m.p99));
            Debug.Log("[Perf] TAMAMLANDI "+JsonUtility.ToJson(summary));
        }
        static string F(double n){return n.ToString("0.####",Inv);}
        void Add(Summary summary,string name,Func<Sample,double> select)
        {
            var values=new List<double>(samples.Count);foreach(var sample in samples){double v=select(sample);if(v>=0&&!double.IsNaN(v)&&!double.IsInfinity(v))values.Add(v);}values.Sort();
            summary.metrics.Add(new Metric{name=name,validSamples=values.Count,p50=Percentile(values,.5),p95=Percentile(values,.95),p99=Percentile(values,.99)});
        }
        static double Percentile(List<double> sorted,double p){return sorted.Count==0?-1:sorted[Math.Min(sorted.Count-1,(int)Math.Ceiling(p*sorted.Count)-1)];}
        void OnDestroy()
        {
            draws.Dispose();passes.Dispose();batches.Dispose();triangles.Dispose();shadows.Dispose();gc.Dispose();mainThread.Dispose();renderThread.Dispose();
            if(samples!=null){RearViewMirror.UserEnabled=oldMirror;Application.SetStackTraceLogType(LogType.Log,oldLog);}
        }
    }
}
