using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    // Keep small atlases in the game's content cache. Decode only the displayed
    // expression from the original PNG, with scanline-bounded memory, and keep
    // one detailed texture alive. Source MOD files are only opened for reading.
    internal static class PortraitTextureCache
    {
        private sealed class Source
        {
            public string source { get; set; }
            public string cacheKey { get; set; }
            public int factor { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public int sourceWidth { get; set; }
            public int sourceHeight { get; set; }
        }
        private sealed class Signature { internal string Hash; }
        private static readonly ConditionalWeakTable<Texture2D, Signature> signatures = new();
        private static Dictionary<string, Source> sources = new();
        private static string directory, dotnet, resizer, activeKey;
        private static Texture2D active;
        private static Rectangle activeBounds;
        private static readonly HashSet<string> failed = new();
        private static long lastUsed;
        internal static int ActiveWidth => active?.Width ?? 0;
        internal static int ActiveHeight => active?.Height ?? 0;
        internal static Rectangle ActiveBounds => activeBounds;
        internal static int DetailDownsampleFactor(int width, int height)
        {
            int largest = Math.Max(width, height);
            return largest > 1200 ? 4 : largest > 400 ? 2 : 1;
        }

        internal static void Initialize(IModHelper helper)
        {
            string runtime = Environment.GetEnvironmentVariable("RGDSPLUS_RUNTIME_MODS_PATH");
            if (string.IsNullOrEmpty(runtime)) return;
            directory = Path.Combine(Directory.GetParent(runtime).FullName, "portrait-display");
            dotnet = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "", "dotnet");
            resizer = Path.Combine(Directory.GetParent(Directory.GetParent(runtime).FullName).FullName,
                "tools", "ModCompatResizer", "RGDSplus.ModCompatResizer.dll");
            try
            {
                using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, ".rgds-runtime.json")));
                if (marker.RootElement.TryGetProperty("portraitSources", out var entries))
                    sources = JsonSerializer.Deserialize<Dictionary<string, Source>>(entries.GetRawText());
            }
            catch (Exception ex) { ModEntry.Write("portrait-detail index: " + ex.Message); }
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => Clear();
            helper.Events.Content.AssetsInvalidated += (_, _) => { signatures.Clear(); failed.Clear(); Clear(); };
            helper.Events.GameLoop.UpdateTicked += (_, _) => {
                if (active != null && Environment.TickCount64 - lastUsed > 2000) Clear();
            };
        }

        private static void Clear()
        {
            active?.Dispose(); active = null; activeKey = null; activeBounds = Rectangle.Empty;
        }

        private static Signature Identify(Texture2D texture)
        {
            var colors = new Color[texture.Width * texture.Height];
            texture.GetData(colors);
            ulong hash = 14695981039346656037UL;
            foreach (var color in colors)
            {
                hash = unchecked((hash ^ (color.A == 255 ? color.R : 0UL)) * 1099511628211UL);
                hash = unchecked((hash ^ (color.A == 255 ? color.G : 0UL)) * 1099511628211UL);
                hash = unchecked((hash ^ (color.A == 255 ? color.B : 0UL)) * 1099511628211UL);
                hash = unchecked((hash ^ color.A) * 1099511628211UL);
            }
            return new Signature { Hash = hash.ToString("x16") };
        }

        internal static Texture2D Get(Texture2D atlas, Rectangle tile)
        {
            if (sources.Count == 0 || atlas == null || directory == null) return null;
            var signature = signatures.GetValue(atlas, Identify);
            if (!sources.TryGetValue(signature.Hash, out var source) || source.width != atlas.Width || source.height != atlas.Height)
                return null;
            int x = tile.X * source.factor, y = tile.Y * source.factor;
            int width = tile.Width * source.factor, height = tile.Height * source.factor;
            if (x < 0 || y < 0 || x + width > source.sourceWidth || y + height > source.sourceHeight) return null;
            string key = source.cacheKey + "-" + x + "-" + y + "-" + width + "-" + height;
            if (failed.Contains(key)) return null;
            lastUsed = Environment.TickCount64;
            if (activeKey == key && active?.IsDisposed == false) return active;
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, key + ".png");
                if (!File.Exists(path))
                {
                    string pending = path + ".next";
                    var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { resizer, "--tile", source.source, pending, x.ToString(), y.ToString(),
                        width.ToString(), height.ToString(), "--factor=" + DetailDownsampleFactor(width, height) })
                        start.ArgumentList.Add(arg);
                    using var process = Process.Start(start);
                    if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("portrait tile decode"); }
                    if (process.ExitCode != 0) throw new InvalidOperationException("portrait tile decoder exit=" + process.ExitCode);
                    File.Move(pending, path, true);
                }
                using var stream = File.OpenRead(path);
                var next = Texture2D.FromStream(Game1.game1.GraphicsDevice, stream);
                var colors = new Color[next.Width * next.Height];
                next.GetData(colors);
                int left = next.Width, top = next.Height, right = 0, bottom = 0;
                for (int row = 0; row < next.Height; row++)
                    for (int col = 0; col < next.Width; col++)
                        if (colors[row * next.Width + col].A != 0)
                        {
                            left = Math.Min(left, col); right = Math.Max(right, col + 1);
                            top = Math.Min(top, row); bottom = Math.Max(bottom, row + 1);
                        }
                var bounds = right > left && bottom > top ? new Rectangle(left, top, right - left, bottom - top) : next.Bounds;
                Clear(); active = next; activeBounds = bounds; activeKey = key; lastUsed = Environment.TickCount64;
                return active;
            }
            catch (Exception ex)
            {
                failed.Add(key);
                ModEntry.Write("portrait-detail fallback: " + ex.Message);
                return null;
            }
        }
    }
}
