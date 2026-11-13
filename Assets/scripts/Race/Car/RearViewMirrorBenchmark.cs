using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Dikiz aynasının maliyetini ölçer. Yerel araba sürülürken çalıştırılır:
    /// editörde execute_code ile RearViewMirrorBenchmark.Run(), build'de -mirrorBench komut satırı bayrağı.
    /// Sırayla aynasız / her 2 karede / her karede / tekrar aynasız durumlarını ölçer ve "[Ayna]" satırları yazar:
    /// CPU ana iş parçacığı, render iş parçacığı ve GPU kare süresi (FrameTimingManager), toplam draw call / SetPass,
    /// karedeki GC ve ayna kamerasının kendi CPU/GPU süresi (URP'nin kamera işaretçisi). Build'de GPU süresi için
    /// Player Settings'te "Frame Timing Stats" açık olmalı.
    /// </summary>
    public class RearViewMirrorBenchmark : MonoBehaviour
    {
        public static bool Running { get; private set; }

        const int WarmupFrames = 60;
        const int SampleFrames = 300;

        struct Stats
        {
            public double cpu, render, gpu, mirrorCpu, mirrorGpu;
            public double drawCalls, setPass, batches, gc;
            public double drawCallsMirrorFrame, setPassMirrorFrame, drawCallsPlainFrame, setPassPlainFrame;
            public int frames, mirrorFrames, plainFrames;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CheckCommandLine()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mirrorBench") >= 0) Run(15f);
        }

        /// <summary>Ölçümü başlatır. delay: yerel arabanın sürülmeye başlamasını beklemek için en az süre (sn).</summary>
        public static void Run(float delay = 0f)
        {
            if (Running) return;
            var go = new GameObject("RearViewMirrorBenchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<RearViewMirrorBenchmark>().StartCoroutine(go.GetComponent<RearViewMirrorBenchmark>().Measure(delay));
        }

        private IEnumerator Measure(float delay)
        {
            Running = true;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < delay || RearViewMirror.Instance == null || RaceCar.Local == null)
            {
                if (Time.realtimeSinceStartup - start > delay + 120f)
                {
                    Debug.LogWarning("[Ayna] Ölçüm iptal: yerel araba bulunamadı.");
                    Finish();
                    yield break;
                }
                yield return null;
            }

            var mirror = RearViewMirror.Instance;
            int oldInterval = mirror.frameInterval;
            bool oldUser = RearViewMirror.UserEnabled;
            RearViewMirror.UserEnabled = true;
            Debug.Log("[Ayna] Ölçüm başlıyor. FrameTimingManager: " + FrameTimingManager.IsFeatureEnabled()
                      + ", arabalar: " + RaceCar.All.Count + ", ekran: " + Screen.width + "x" + Screen.height
                      + ", kamera modu: " + (CarCamera.Current != null ? CarCamera.Current.CurrentMode.ToString() : "-"));

            string[] names = { "aynasiz", "her 2 karede", "her karede", "aynasiz (tekrar)" };
            int[] intervals = { 0, 2, 1, 0 };
            for (int i = 0; i < names.Length; i++)
            {
                RearViewMirror.Suppressed = intervals[i] == 0;
                if (intervals[i] > 0) mirror.frameInterval = intervals[i];
                var stats = new Stats();
                yield return Sample(mirror, stats, s => stats = s);
                Report(names[i], stats);
            }

            mirror.frameInterval = oldInterval;
            RearViewMirror.UserEnabled = oldUser;
            RearViewMirror.Suppressed = false;
            Debug.Log("[Ayna] Ölçüm bitti.");
            Finish();
        }

        private IEnumerator Sample(RearViewMirror mirror, Stats stats, System.Action<Stats> done)
        {
            const string marker = "UniversalRenderPipeline.RenderSingleCameraInternal: " + RearViewMirror.CameraName;
            var drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var mirrorCpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, marker);
            var mirrorGpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, marker, 1, ProfilerRecorderOptions.GpuRecorder);
            var timings = new FrameTiming[1];

            for (int i = 0; i < WarmupFrames; i++) yield return null;

            bool renderedLastFrame = mirror.RendersThisFrame;
            for (int i = 0; i < SampleFrames; i++)
            {
                yield return null;
                // Kayıtçılar bir önceki karenin değerini verir
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    stats.cpu += timings[0].cpuMainThreadFrameTime;
                    stats.render += timings[0].cpuRenderThreadFrameTime;
                    stats.gpu += timings[0].gpuFrameTime;
                }
                long dc = drawCalls.LastValue, sp = setPass.LastValue;
                stats.drawCalls += dc;
                stats.setPass += sp;
                stats.batches += batches.LastValue;
                stats.gc += gc.LastValue;
                stats.mirrorCpu += mirrorCpu.LastValue * 1e-6;
                stats.mirrorGpu += mirrorGpu.LastValue * 1e-6;
                if (renderedLastFrame)
                {
                    stats.drawCallsMirrorFrame += dc;
                    stats.setPassMirrorFrame += sp;
                    stats.mirrorFrames++;
                }
                else
                {
                    stats.drawCallsPlainFrame += dc;
                    stats.setPassPlainFrame += sp;
                    stats.plainFrames++;
                }
                stats.frames++;
                renderedLastFrame = mirror.RendersThisFrame;
            }

            drawCalls.Dispose(); setPass.Dispose(); batches.Dispose(); gc.Dispose(); mirrorCpu.Dispose(); mirrorGpu.Dispose();
            done(stats);
        }

        private static void Report(string name, Stats s)
        {
            double n = Mathf.Max(1, s.frames);
            var sb = new StringBuilder();
            sb.Append("[Ayna] ").Append(name)
              .Append(" | CPU ").Append((s.cpu / n).ToString("0.000"))
              .Append(" ms, render ").Append((s.render / n).ToString("0.000"))
              .Append(" ms, GPU ").Append((s.gpu / n).ToString("0.000"))
              .Append(" ms | draw ").Append((s.drawCalls / n).ToString("0.0"))
              .Append(", setpass ").Append((s.setPass / n).ToString("0.0"))
              .Append(", batch ").Append((s.batches / n).ToString("0.0"))
              .Append(" | GC/kare ").Append((s.gc / n).ToString("0")).Append(" B")
              .Append(" | ayna kamerasi CPU ").Append((s.mirrorCpu / n).ToString("0.000"))
              .Append(" ms, GPU ").Append((s.mirrorGpu / n).ToString("0.000")).Append(" ms (kare ortalaması)");
            if (s.mirrorFrames > 0 && s.plainFrames > 0)
            {
                sb.Append(" | ayna karesi ek draw ").Append((s.drawCallsMirrorFrame / s.mirrorFrames - s.drawCallsPlainFrame / s.plainFrames).ToString("0.0"))
                  .Append(", ek setpass ").Append((s.setPassMirrorFrame / s.mirrorFrames - s.setPassPlainFrame / s.plainFrames).ToString("0.0"));
            }
            Debug.Log(sb.ToString());
        }

        private void Finish()
        {
            Running = false;
            Destroy(gameObject);
        }
    }
}
