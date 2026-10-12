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
    // Store next-launch choices only. The launcher applies directory changes
    // before SMAPI starts, so loaded mods keep their resources for this session.
    internal static class ModManagement
    {
        internal const string DisabledPrefix = ".rgds-disabled--";
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
            internal bool OnDisk, Loaded, Locked;
            internal string Folder;
        }
        private static readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive=true, AllowTrailingCommas=true, ReadCommentHandling=JsonCommentHandling.Skip, WriteIndented=true };
        private static IModHelper helper;
        private static string root, statePath;
        private static List<Entry> entries = new();
        private static Dictionary<string,bool> choices = new(StringComparer.OrdinalIgnoreCase);
        private static int offset;
        private static long refreshAt;
        private static string error;
        internal static bool IsLocked(string id) => id.Equals("RGDSplus.VerticalWorld",StringComparison.OrdinalIgnoreCase)
            || id.Equals("SMAPI.ConsoleCommands",StringComparison.OrdinalIgnoreCase) || id.Equals("SMAPI.SaveBackup",StringComparison.OrdinalIgnoreCase)
            || id.Equals("RGDSplus.PerformanceProbe",StringComparison.OrdinalIgnoreCase) || id.Equals("RGDSplus.RenderScaleProbe",StringComparison.OrdinalIgnoreCase);
        internal static void Initialize(IModHelper value)
        {
            helper=value; root=Directory.GetParent(helper.DirectoryPath).FullName;
            statePath=Path.Combine(root,".rgds-mod-state.json");
        }
        internal static void Refresh(bool force=false)
        {
            if (helper==null || !force && Environment.TickCount64<refreshAt) return;
            refreshAt=Environment.TickCount64+3000;
            try
            {
                choices=File.Exists(statePath) ? new Dictionary<string,bool>(JsonSerializer.Deserialize<Dictionary<string,bool>>(File.ReadAllText(statePath),json),StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
                var found=new List<Entry>();
                Scan(new DirectoryInfo(root),found,0);
                if(found.GroupBy(e=>e.Manifest.UniqueID,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)) throw new InvalidOperationException("Duplicate mod IDs");
                entries=found.OrderByDescending(e=>e.Locked).ThenBy(e=>e.Manifest.Name).ToList();
                offset=Math.Clamp(offset,0,Math.Max(0,entries.Count-4)); error=null;
            }
            catch(Exception ex) { error=ex.Message; ModEntry.Write("mod-list: "+ex.Message); }
        }
        private static void Scan(DirectoryInfo folder,List<Entry> found,int depth)
        {
            if(depth>8 || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if(depth>0 && folder.Name.StartsWith(".") && !folder.Name.StartsWith(DisabledPrefix,StringComparison.Ordinal)) return;
            var path=folder.GetFiles().FirstOrDefault(f=>f.Name.Equals("manifest.json",StringComparison.OrdinalIgnoreCase));
            if(path!=null)
            {
                var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path.FullName),json);
                if(string.IsNullOrWhiteSpace(manifest?.UniqueID)) throw new InvalidOperationException("Invalid manifest: "+folder.Name);
                found.Add(new Entry { Manifest=manifest, Folder=folder.FullName, OnDisk=!folder.Name.StartsWith(DisabledPrefix,StringComparison.Ordinal),
                    Loaded=helper.ModRegistry.IsLoaded(manifest.UniqueID), Locked=IsLocked(manifest.UniqueID) });
                return;
            }
            foreach(var child in folder.GetDirectories()) Scan(child,found,depth+1);
        }
        private static bool Desired(Entry entry) => entry.Locked || (choices.TryGetValue(entry.Manifest.UniqueID,out bool value) ? value : entry.OnDisk);
        private static IEnumerable<Dependency> Required(Entry entry) => (entry.Manifest.Dependencies??Array.Empty<Dependency>()).Where(d=>d.IsRequired)
            .Concat(entry.Manifest.ContentPackFor==null ? Array.Empty<Dependency>() : new[] {entry.Manifest.ContentPackFor});
        internal static string ValidateToggle(Entry target,IReadOnlyList<Entry> all,Func<Entry,bool> enabled,bool turnOn)
        {
            if(target.Locked) return Dashboard.Text("基础组件固定开启","Required component stays enabled");
            if(turnOn)
            {
                foreach(var need in Required(target))
                {
                    var dependency=all.FirstOrDefault(e=>string.Equals(e.Manifest.UniqueID,need.UniqueID,StringComparison.OrdinalIgnoreCase));
                    if(dependency==null || !enabled(dependency)) return Dashboard.Text("请先启用依赖：","Enable dependency first: ")+need.UniqueID;
                    if(!string.IsNullOrWhiteSpace(need.MinimumVersion) && new SemanticVersion(dependency.Manifest.Version).IsOlderThan(need.MinimumVersion))
                        return Dashboard.Text("依赖版本过低：","Dependency version too old: ")+need.UniqueID;
                }
            }
            else
            {
                var dependent=all.FirstOrDefault(e=>e!=target && enabled(e) && Required(e).Any(d=>string.Equals(d.UniqueID,target.Manifest.UniqueID,StringComparison.OrdinalIgnoreCase)));
                if(dependent!=null) return Dashboard.Text("请先关闭依赖它的 MOD：","Disable dependent mod first: ")+dependent.Manifest.Name;
            }
            return null;
        }
        internal static void Scroll(int step) { Refresh(); offset=Math.Clamp(offset+step,0,Math.Max(0,entries.Count-4)); }
        internal static object Status()
        {
            Refresh();
            return new { error, entries=entries.Select(e=>new {id=e.Manifest.UniqueID,name=e.Manifest.Name,loaded=e.Loaded,enabled=Desired(e),locked=e.Locked,pending=Desired(e)!=e.OnDisk}).ToArray() };
        }
        internal static void Draw(SpriteBatch b)
        {
            Refresh();
            Dashboard.Write(b,Dashboard.Text("MOD 管理","MOD management"),Dashboard.Rect(40,106,800,42),Dashboard.Ink);
            Dashboard.Paragraph(b,Dashboard.Text("开关在退出到桌面、重新进入后生效。返回标题不应用开关。",
                "Exit to desktop and relaunch to apply changes. Returning to title keeps the current mod set."),Dashboard.Rect(40,156,936,70),Dashboard.Muted);
            if(error!=null) { Dashboard.Paragraph(b,Dashboard.Text("无法读取 MOD 列表：","Unable to read mods: ")+error,Dashboard.Rect(40,252,936,220),Dashboard.Ink); return; }
            for(int row=0;row<4 && offset+row<entries.Count;row++)
            {
                var entry=entries[offset+row]; int y=244+row*100;
                Dashboard.Panel(b,Dashboard.Rect(28,y,968,92),Dashboard.Card,Dashboard.Muted);
                Dashboard.Write(b,(entry.Manifest.Name??entry.Manifest.UniqueID)+"  "+entry.Manifest.Version,Dashboard.Rect(44,y+6,682,36),Dashboard.Ink,.94f);
                string state=entry.Loaded?Dashboard.Text("本次已加载","Loaded this session"):Dashboard.Text("本次未加载","Not loaded this session");
                if(Desired(entry)!=entry.OnDisk) state+=Dashboard.Text(" · 待重启"," · Restart pending");
                Dashboard.Write(b,state,Dashboard.Rect(44,y+48,682,30),Dashboard.Muted,.84f);
                Dashboard.Button(b,entry.Locked?Dashboard.Text("固定开启","Required"):Desired(entry)?Dashboard.Text("开启","On"):Dashboard.Text("关闭","Off"),Dashboard.Rect(752,y+20,224,54),Desired(entry));
            }
            if(entries.Count==0) Dashboard.Write(b,Dashboard.Text("尚未安装 MOD","No mods installed"),Dashboard.Rect(44,280,900,60),Dashboard.Ink);
            Dashboard.Button(b,"<",Dashboard.Rect(28,668,64,40)); Dashboard.Button(b,">",Dashboard.Rect(932,668,64,40));
            Dashboard.Write(b,Dashboard.Text("安装到 Mods 文件夹后会出现在此处","Mods installed in the Mods folder appear here"),Dashboard.Rect(112,668,800,40),Dashboard.Muted,.84f);
        }
        internal static bool Click(int x,int y)
        {
            Refresh();
            if(Dashboard.Rect(28,668,64,40).Contains(x,y)) {Scroll(-4);return true;}
            if(Dashboard.Rect(932,668,64,40).Contains(x,y)) {Scroll(4);return true;}
            if(error!=null) return true;
            for(int row=0;row<4 && offset+row<entries.Count;row++)
            {
                if(!Dashboard.Rect(752,264+row*100,224,54).Contains(x,y)) continue;
                var entry=entries[offset+row]; bool value=!Desired(entry);
                try
                {
                    string reason=ValidateToggle(entry,entries,Desired,value);
                    if(reason!=null) {Dashboard.Notice=reason;return true;}
                    var next=new Dictionary<string,bool>(choices,StringComparer.OrdinalIgnoreCase) {[entry.Manifest.UniqueID]=value};
                    File.WriteAllText(statePath+".next",JsonSerializer.Serialize(next,json));
                    File.Move(statePath+".next",statePath,true); choices=next;
                    Dashboard.Notice=Dashboard.Text("已保存，将在下次启动游戏时生效","Saved; applies on the next game launch");
                }
                catch(Exception ex) {Dashboard.Notice=Dashboard.Text("未能保存：","Could not save: ")+ex.Message;}
                return true;
            }
            return true;
        }
    }
}
