using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    // Prepare measured code paths only; never invoke tools, load a farm, or
    // execute gameplay to warm it. Opt-in until device acceptance completes.
    internal static class FirstUseWarmup
    {
        internal static bool Enabled => Environment.GetEnvironmentVariable("SDV_FIRST_USE_WARMUP") == "1";
        private static bool completed;
        internal static void Initialize(IModHelper helper)
        {
            if (!Enabled) return;
            helper.Events.GameLoop.GameLaunched += (_, _) =>
            {
                try { Prepare(); }
                catch (Exception ex) { ModEntry.Write("first-use-warmup skipped: " + ex); }
            };
        }
        internal static object Prepare()
        {
            if (completed) return new { alreadyPrepared = true };
            completed = true;
            var timer = Stopwatch.StartNew();
            long allocated = GC.GetTotalAllocatedBytes(false);
            long resident = Process.GetCurrentProcess().WorkingSet64;
            var prepared = new HashSet<MethodBase>();
            var failures = new List<string>();
            using var stream = typeof(FirstUseWarmup).Assembly.GetManifestResourceStream("Rgdsplus.FirstUseMethods.txt");
            using var reader = new StreamReader(stream ?? throw new InvalidOperationException("Missing warmup manifest"));
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a =>
                a.GetName().Name is "Stardew Valley" or "MonoGame.Framework" or "Netcode" or "StardewValley.GameData").ToArray();
            string line;
            while ((line = reader.ReadLine()) != null && timer.ElapsedMilliseconds < 4500 && prepared.Count < 400)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('|');
                if (parts.Length != 2) continue;
                var type = assemblies.Select(a => a.GetType(parts[0], false)).FirstOrDefault(t => t != null);
                if (type == null || type.ContainsGenericParameters) { failures.Add(line + ":missing-type"); continue; }
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                IEnumerable<MethodBase> methods = parts[1] == ".ctor" ? type.GetConstructors(flags) : type.GetMethods(flags).Where(m => m.Name == parts[1]);
                foreach (var method in methods)
                {
                    if (timer.ElapsedMilliseconds >= 4500 || prepared.Count >= 400) break;
                    if (method.IsAbstract || method.ContainsGenericParameters || !prepared.Add(method)) continue;
                    try { RuntimeHelpers.PrepareMethod(method.MethodHandle); }
                    catch (Exception ex) { failures.Add(line + ":" + ex.GetType().Name); }
                }
            }
            var result = new { ms = timer.Elapsed.TotalMilliseconds, methods = prepared.Count, failures,
                allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
                residentDeltaBytes = Process.GetCurrentProcess().WorkingSet64 - resident };
            string json = JsonSerializer.Serialize(result);
            ModEntry.Write("first-use-warmup " + json);
            string qa = Environment.GetEnvironmentVariable("SDV_DUAL_PERF_TEST_DIR");
            if (!string.IsNullOrEmpty(qa)) File.WriteAllText(Path.Combine(qa, "warmup.json"), json);
            return result;
        }
    }
}
