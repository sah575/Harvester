using System;
using System.Reflection;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// HarvesterAI'ya ince bağlantı. HarvesterAI ayrı ajan tarafından paralel yazıldığı için tür burada
    /// yansımayla (reflection) bulunur; böylece bu klasör HarvesterAI olsa da olmasa da derlenir.
    /// Beklenen sözleşme (Harvester.Race.HarvesterAI):
    ///   static HarvesterAI Instance { get; }
    ///   void SetRunning(bool)  ya da  bool Running { get; set; }
    ///   void PlaceAtStart(int racerCount = 4)
    /// Birleştirmede istenirse bu sınıfın gövdesi doğrudan çağrılarla değiştirilebilir; çağıranlar değişmez.
    /// </summary>
    public static class HarvesterLink
    {
        private static bool resolved;
        private static Type type;
        private static PropertyInfo instanceProperty;
        private static FieldInfo instanceField;
        private static MethodInfo setRunningMethod;
        private static PropertyInfo runningProperty;
        private static MethodInfo placeAtStartMethod;
        private static bool warned;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType("Harvester.Race.HarvesterAI");
                if (type != null) break;
            }
            if (type == null) return;
            const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags I = BindingFlags.Public | BindingFlags.Instance;
            instanceProperty = type.GetProperty("Instance", S);
            if (instanceProperty == null) instanceField = type.GetField("Instance", S);
            setRunningMethod = type.GetMethod("SetRunning", I, null, new[] { typeof(bool) }, null);
            runningProperty = type.GetProperty("Running", I);
            foreach (var m in type.GetMethods(I))
            {
                if (m.Name != "PlaceAtStart") continue;
                var ps = m.GetParameters();
                if (ps.Length == 0 || (ps.Length == 1 && ps[0].ParameterType == typeof(int))) { placeAtStartMethod = m; break; }
            }
        }

        private static void Warn(string what)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[Race] HarvesterAI bağlantısı eksik: " + what);
        }

        /// <summary>Sahnedeki harvester yapay zekâsı (yoksa null).</summary>
        public static Component Instance
        {
            get
            {
                Resolve();
                if (type == null) return null;
                object value = instanceProperty != null ? instanceProperty.GetValue(null)
                             : instanceField != null ? instanceField.GetValue(null) : null;
                var component = value as Component;
                return component != null ? component : null; // yok edilmiş Unity objesi de null sayılsın
            }
        }

        public static bool Exists => Instance != null;

        /// <summary>Sadece sunucu: harvester'ı durdurur ya da kaldırır.</summary>
        public static void SetRunning(bool running)
        {
            var instance = Instance;
            if (instance == null) return;
            if (setRunningMethod != null) setRunningMethod.Invoke(instance, new object[] { running });
            else if (runningProperty != null && runningProperty.CanWrite) runningProperty.SetValue(instance, running);
            else Warn("SetRunning(bool) ya da Running yok");
        }

        /// <summary>Sadece sunucu: harvester'ı başlangıç yerine koyar.</summary>
        public static void PlaceAtStart(int racerCount)
        {
            var instance = Instance;
            if (instance == null) return;
            if (placeAtStartMethod != null)
                placeAtStartMethod.Invoke(instance, placeAtStartMethod.GetParameters().Length == 1 ? new object[] { racerCount } : null);
            else Warn("PlaceAtStart() yok");
        }
    }
}
