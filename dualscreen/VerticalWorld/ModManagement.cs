using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace Rgdsplus.VerticalWorld
{
    // The page reads the user's Mods directory. Choices are applied by the
    // launcher when it builds the private runtime directory for the next run.
    internal static class ModManagement
    {
        private const string StateFile = "mod-state.json";
        private const string CompatibilityFolder = "RGDSplus.ModCompatibility--";
        internal sealed class Dependency
        {
            public string UniqueID { get; set; }
            public string MinimumVersion { get; set; }
            public bool IsRequired { get; set; } = true;
        }
        internal sealed class Manifest
        {
            public string UniqueID { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public Dependency[] Dependencies { get; set; } = Array.Empty<Dependency>();
            public Dependency ContentPackFor { get; set; }
        }
        internal sealed class Entry
        {
            internal Manifest Manifest;
            internal bool RuntimeEnabled, Loaded, Locked, DefaultEnabled = true;
            internal string Folder;
        }
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };
        private static IModHelper helper;
        private static string root, statePath;
        private static List<Entry> entries = new();
        private static Dictionary<string, bool> choices = new(StringComparer.OrdinalIgnoreCase);
        private static int offset;
        private static long refreshAt;
        private static string error, warning;

        internal static bool IsLocked(string id) => id.Equals("RGDSplus.VerticalWorld", StringComparison.OrdinalIgnoreCase)
            || id.Equals("SMAPI.ConsoleCommands", StringComparison.OrdinalIgnoreCase)
            || id.Equals("SMAPI.SaveBackup", StringComparison.OrdinalIgnoreCase)
            || id.Equals("RGDSplus.PerformanceProbe", StringComparison.OrdinalIgnoreCase)
            || id.Equals("RGDSplus.RenderScaleProbe", StringComparison.OrdinalIgnoreCase);

        internal static void Initialize(IModHelper value)
        {
            helper = value;
            var configured = Environment.GetEnvironmentVariable("RGDSPLUS_USER_MODS_PATH");
            root = string.IsNullOrWhiteSpace(configured)
                ? Directory.GetParent(helper.DirectoryPath).FullName
                : Path.GetFullPath(configured);
            var configuredState = Environment.GetEnvironmentVariable("RGDSPLUS_MOD_STATE_PATH");
            var fallbackRuntimeRoot = Directory.GetParent(Directory.GetParent(helper.DirectoryPath).FullName).FullName;
            statePath = string.IsNullOrWhiteSpace(configuredState)
                ? Path.Combine(fallbackRuntimeRoot, StateFile)
                : Path.GetFullPath(configuredState);
        }

        internal static void Refresh(bool force = false)
        {
            if (helper == null || (!force && Environment.TickCount64 < refreshAt)) return;
            refreshAt = Environment.TickCount64 + 3000;
            try
            {
                var savedChoices = File.Exists(statePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(statePath), Json)
                    : null;
                choices = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (savedChoices != null)
                    foreach (var pair in savedChoices) choices[pair.Key] = pair.Value;
                var found = new List<Entry>();
                var warnings = new List<string>();
                Scan(new DirectoryInfo(root), found, warnings, 0);
                var runtimeIds = ScanRuntimeIds(warnings);
                foreach (var entry in found) entry.RuntimeEnabled = runtimeIds.Contains(entry.Manifest.UniqueID);
                entries = found.OrderByDescending(e => e.Locked).ThenBy(e => e.Manifest.Name ?? e.Manifest.UniqueID, StringComparer.OrdinalIgnoreCase).ToList();
                offset = Math.Clamp(offset, 0, Math.Max(0, entries.Count - 4));
                error = null;
                warning = warnings.Count == 0 ? null : string.Join("；", warnings.Take(3));
                if (warnings.Count > 3) warning += "；…";
                if (warnings.Count > 0) ModEntry.Write("mod-list warnings=" + string.Join(" | ", warnings));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                warning = null;
                ModEntry.Write("mod-list: " + ex);
            }
        }

        private static HashSet<string> ScanRuntimeIds(List<string> warnings)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var configured = Environment.GetEnvironmentVariable("RGDSPLUS_RUNTIME_MODS_PATH");
            if (string.IsNullOrWhiteSpace(configured)) return result;
            try
            {
                var runtime = new DirectoryInfo(Path.GetFullPath(configured));
                if (!runtime.Exists) return result;
                ScanRuntimeFolder(runtime, 0, result, warnings);
            }
            catch (Exception ex) { warnings.Add("runtime: " + ex.Message); }
            return result;
        }

        private static void ScanRuntimeFolder(DirectoryInfo folder, int depth, HashSet<string> ids, List<string> warnings)
        {
            if (depth > 8 || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            FileInfo file;
            try { file = folder.GetFiles().FirstOrDefault(f => f.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)); }
            catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); return; }
            if (file != null)
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file.FullName), Json);
                    if (!string.IsNullOrWhiteSpace(manifest?.UniqueID)) ids.Add(manifest.UniqueID);
                }
                catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); }
                return;
            }
            DirectoryInfo[] children;
            try { children = folder.GetDirectories(); }
            catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); return; }
            foreach (var child in children) ScanRuntimeFolder(child, depth + 1, ids, warnings);
        }

        private static void Scan(DirectoryInfo folder, List<Entry> found, List<string> warnings, int depth)
        {
            if (depth > 8 || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (depth > 0 && ((folder.Name.StartsWith(".", StringComparison.Ordinal)
                && !folder.Name.StartsWith(".rgds-disabled--", StringComparison.Ordinal))
                || folder.Name.StartsWith(CompatibilityFolder, StringComparison.OrdinalIgnoreCase))) return;
            FileInfo manifestFile;
            try { manifestFile = folder.GetFiles().FirstOrDefault(f => f.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)); }
            catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); return; }
            if (manifestFile != null)
            {
                try
                {
                    if (manifestFile.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("symlink manifest");
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestFile.FullName), Json);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.UniqueID)) throw new InvalidDataException("missing UniqueID");
                    if (found.Any(entry => entry.Manifest.UniqueID.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase)))
                    {
                        warnings.Add(folder.Name + ": duplicate UniqueID " + manifest.UniqueID);
                        return;
                    }
                    found.Add(new Entry
                    {
                        Manifest = manifest,
                        Folder = folder.FullName,
                        Loaded = helper.ModRegistry.IsLoaded(manifest.UniqueID),
                        Locked = IsLocked(manifest.UniqueID),
                        DefaultEnabled = !folder.Name.StartsWith(".rgds-disabled--", StringComparison.Ordinal)
                            || (new[] { "OO.Portraits", "OO.Portraits2", "Mud.SkimpyPortraits", "Mud.SkimpyPortraits2" }.Contains(manifest.UniqueID, StringComparer.OrdinalIgnoreCase)
                                && Directory.Exists(Path.Combine(folder.Parent.FullName, CompatibilityFolder + manifest.UniqueID)))
                    });
                }
                catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); }
                return;
            }
            DirectoryInfo[] children;
            try { children = folder.GetDirectories(); }
            catch (Exception ex) { warnings.Add(folder.Name + ": " + ex.Message); return; }
            foreach (var child in children.OrderBy(d => d.Name.StartsWith(".rgds-disabled--", StringComparison.Ordinal)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                Scan(child, found, warnings, depth + 1);
        }

        private static bool Desired(Entry entry) => entry.Locked ||
            (choices.TryGetValue(entry.Manifest.UniqueID, out var value) ? value : entry.DefaultEnabled);

        private static IEnumerable<Dependency> Required(Entry entry) => (entry.Manifest.Dependencies ?? Array.Empty<Dependency>()).Where(d => d != null && d.IsRequired)
            .Concat(entry.Manifest.ContentPackFor == null ? Array.Empty<Dependency>() : new[] { entry.Manifest.ContentPackFor });

        internal static string ValidateToggle(Entry target, IReadOnlyList<Entry> all, Func<Entry, bool> enabled, bool turnOn)
        {
            if (target.Locked) return Dashboard.Text("基础组件固定开启", "Required component stays enabled");
            if (turnOn)
            {
                foreach (var need in Required(target))
                {
                    var dependency = all.FirstOrDefault(e => string.Equals(e.Manifest.UniqueID, need.UniqueID, StringComparison.OrdinalIgnoreCase));
                    if (dependency == null || !enabled(dependency)) return Dashboard.Text("请先启用依赖：", "Enable dependency first: ") + need.UniqueID;
                    if (!string.IsNullOrWhiteSpace(need.MinimumVersion) && new SemanticVersion(dependency.Manifest.Version).IsOlderThan(need.MinimumVersion))
                        return Dashboard.Text("依赖版本过低：", "Dependency version too old: ") + need.UniqueID;
                }
            }
            else
            {
                var dependent = all.FirstOrDefault(e => e != target && enabled(e) && Required(e).Any(d => string.Equals(d.UniqueID, target.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase)));
                if (dependent != null) return Dashboard.Text("请先关闭依赖它的 MOD：", "Disable dependent mod first: ") + dependent.Manifest.Name;
            }
            return null;
        }

        internal static void Scroll(int step) { Refresh(); offset = Math.Clamp(offset + step, 0, Math.Max(0, entries.Count - 4)); }

        internal static object Status()
        {
            Refresh();
            return new
            {
                error,
                warning,
                entries = entries.Select(e => new { id = e.Manifest.UniqueID, name = e.Manifest.Name, loaded = e.Loaded, enabled = Desired(e), locked = e.Locked, pending = Desired(e) != e.RuntimeEnabled }).ToArray()
            };
        }

        internal static void Draw(SpriteBatch b)
        {
            Refresh();
            Dashboard.Write(b, Dashboard.Text("MOD 管理", "MOD management"), Dashboard.Rect(40, 106, 800, 42), Dashboard.Ink);
            Dashboard.Paragraph(b, Dashboard.Text("开关写入发布包运行时配置，退出到桌面并重新进入后生效。原 MOD 文件保持不变。", "Choices apply on the next launch; source MOD files stay untouched."), Dashboard.Rect(40, 156, 936, 70), Dashboard.Muted);
            if (error != null) { Dashboard.Paragraph(b, Dashboard.Text("无法读取 MOD 列表：", "Unable to read mods: ") + error, Dashboard.Rect(40, 252, 936, 220), Dashboard.Ink); return; }
            if (warning != null) Dashboard.Paragraph(b, Dashboard.Text("扫描提示：", "Scan note: ") + warning, Dashboard.Rect(40, 220, 936, 24), Dashboard.Muted);
            int firstRow = warning == null ? 244 : 270;
            for (int row = 0; row < 4 && offset + row < entries.Count; row++)
            {
                var entry = entries[offset + row]; int y = firstRow + row * 100;
                Dashboard.Panel(b, Dashboard.Rect(28, y, 968, 92), Dashboard.Card, Dashboard.Muted);
                Dashboard.Write(b, (entry.Manifest.Name ?? entry.Manifest.UniqueID) + "  " + entry.Manifest.Version, Dashboard.Rect(44, y + 6, 682, 36), Dashboard.Ink, .94f);
                string state = entry.Loaded ? Dashboard.Text("本次已加载", "Loaded this session") : Dashboard.Text("本次未加载", "Not loaded this session");
                if (Desired(entry) != entry.RuntimeEnabled) state += Dashboard.Text(" · 待重启", " · Restart pending");
                Dashboard.Write(b, state, Dashboard.Rect(44, y + 48, 682, 30), Dashboard.Muted, .84f);
                Dashboard.Button(b, entry.Locked ? Dashboard.Text("固定开启", "Required") : Desired(entry) ? Dashboard.Text("开启", "On") : Dashboard.Text("关闭", "Off"), Dashboard.Rect(752, y + 20, 224, 54), Desired(entry));
            }
            if (entries.Count == 0) Dashboard.Write(b, Dashboard.Text("尚未安装 MOD", "No mods installed"), Dashboard.Rect(44, 280, 900, 60), Dashboard.Ink);
            Dashboard.Button(b, "<", Dashboard.Rect(28, 668, 64, 40)); Dashboard.Button(b, ">", Dashboard.Rect(932, 668, 64, 40));
            Dashboard.Write(b, Dashboard.Text("安装到 Mods 文件夹后会出现在此处", "Mods installed in the Mods folder appear here"), Dashboard.Rect(112, 668, 800, 40), Dashboard.Muted, .84f);
        }

        internal static bool Click(int x, int y)
        {
            Refresh();
            if (Dashboard.Rect(28, 668, 64, 40).Contains(x, y)) { Scroll(-4); return true; }
            if (Dashboard.Rect(932, 668, 64, 40).Contains(x, y)) { Scroll(4); return true; }
            if (error != null) return true;
            int firstRow = warning == null ? 244 : 270;
            for (int row = 0; row < 4 && offset + row < entries.Count; row++)
            {
                if (!Dashboard.Rect(752, firstRow + 20 + row * 100, 224, 54).Contains(x, y)) continue;
                var entry = entries[offset + row]; bool value = !Desired(entry);
                try
                {
                    string reason = ValidateToggle(entry, entries, Desired, value);
                    if (reason != null) { Dashboard.Notice = reason; return true; }
                    var next = new Dictionary<string, bool>(choices, StringComparer.OrdinalIgnoreCase) { [entry.Manifest.UniqueID] = value };
                    File.WriteAllText(statePath + ".next", JsonSerializer.Serialize(next, Json));
                    File.Move(statePath + ".next", statePath, true);
                    choices = next;
                    Dashboard.Notice = Dashboard.Text("已保存，将在下次启动游戏时生效", "Saved; applies on the next game launch");
                }
                catch (Exception ex) { Dashboard.Notice = Dashboard.Text("未能保存：", "Could not save: ") + ex.Message; }
                return true;
            }
            return true;
        }
    }
}
