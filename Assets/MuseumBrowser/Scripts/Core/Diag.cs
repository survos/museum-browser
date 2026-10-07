using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MuseumBrowser.Core
{
    /// Diagnostics for the in-app debug panel: a ring buffer of log lines (everything
    /// sent to Unity's log, including exceptions) and the requests still in flight.
    public static class Diag
    {
        const int MaxLines = 60;
        static readonly LinkedList<string> lines = new();
        static readonly Dictionary<string, Stopwatch> pending = new();

        public static IEnumerable<string> Lines => lines;
        public static IEnumerable<KeyValuePair<string, Stopwatch>> Pending => pending;
        public static int PendingCount => pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            lines.Clear();
            pending.Clear();
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            string prefix = type switch { LogType.Error or LogType.Exception or LogType.Assert => "ERR ", LogType.Warning => "WARN ", _ => "" };
            lines.AddLast($"{Time.realtimeSinceStartup,7:0.0}s {prefix}{message}");
            while (lines.Count > MaxLines) lines.RemoveFirst();
        }

        public static void Begin(string url) => pending[url] = Stopwatch.StartNew();

        /// Ends a request and logs it: outcome, time, size.
        public static void End(string url, string outcome, long bytes)
        {
            long ms = pending.TryGetValue(url, out var sw) ? sw.ElapsedMilliseconds : -1;
            pending.Remove(url);
            var line = $"{outcome} {ms} ms {bytes / 1024f:0.#} KB {Short(url)}";
            if (outcome == "OK") UnityEngine.Debug.Log(line); else UnityEngine.Debug.LogWarning(line);
        }

        public static string Short(string url) => url.Length > 90 ? url.Substring(0, 50) + "…" + url.Substring(url.Length - 36) : url;
    }
}
