using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    internal static class FrameMetrics
    {
        private static string path;
        private static long frameStart, previousFrame, windowStart, updateStart, worldStart, presentStart;
        private static double updateMs, drawMs, worldMs, presentMs;
        private static readonly double[] intervals = new double[2048];
        private static int count, updates, frames, gc0;
        private static int activeFrames;
        private static object windowLocation;
        private static Type windowMenu;
        private static float windowZoom, windowUiScale;
        private static bool mixedContext;
        private static LowerScreenMode windowMode;
        internal static int TargetAllocations;
        internal static void Initialize(string directory)
        {
            path = Path.Combine(directory, "frame-metrics.csv");
            try
            {
                if (File.Exists(path))
                    File.Move(path, Path.Combine(directory, "frame-metrics-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".csv"));
                File.WriteAllText(path, "utc,location,menu,passes,frames,fps,frame_p50_ms,frame_p95_ms,update_avg_ms,draw_avg_ms,world_avg_ms,present_avg_ms,managed_mb,gc0,target_allocations,active_frames,inactive_sleep_ms,zoom,ui_scale,mixed_context,layout\n");
            }
            catch { path = null; }
            windowStart = Stopwatch.GetTimestamp();
            gc0 = GC.CollectionCount(0);
        }
        private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        internal static void BeginUpdate() => updateStart = Stopwatch.GetTimestamp();
        internal static void EndUpdate() { updateMs += Milliseconds(Stopwatch.GetTimestamp() - updateStart); updates++; }
        internal static void BeginWorld() => worldStart = Stopwatch.GetTimestamp();
        internal static void EndWorld() => worldMs += Milliseconds(Stopwatch.GetTimestamp() - worldStart);
        internal static void BeginPresent() => presentStart = Stopwatch.GetTimestamp();
        internal static void EndPresent() => presentMs += Milliseconds(Stopwatch.GetTimestamp() - presentStart);
        internal static void BeginFrame()
        {
            frameStart = Stopwatch.GetTimestamp();
            // Exclude windows spanning a load/menu/zoom change from scene comparisons.
            var location = Game1.currentLocation;
            var menu = Game1.activeClickableMenu?.GetType();
            float zoom = Game1.options?.zoomLevel ?? 1f;
            float uiScale = Game1.options?.uiScale ?? 1f;
            if (frames == 0)
            {
                windowLocation = location;
                windowMenu = menu;
                windowZoom = zoom;
                windowUiScale = uiScale;
                windowMode = DisplayModeController.State.Mode;
            }
            else if (!ReferenceEquals(windowLocation, location) || windowMenu != menu || windowZoom != zoom || windowUiScale != uiScale || windowMode != DisplayModeController.State.Mode)
                mixedContext = true;
            if (GameRunner.instance?.IsActive == true) activeFrames++;
            if (previousFrame != 0 && count < intervals.Length) intervals[count++] = Milliseconds(frameStart - previousFrame);
            previousFrame = frameStart;
        }
        internal static void EndFrame()
        {
            NativeTouch.AfterFrame();
            drawMs += Milliseconds(Stopwatch.GetTimestamp() - frameStart);
            frames++;
            double elapsed = Milliseconds(frameStart - windowStart);
            if (elapsed < 10000 || count == 0 || path == null) return;
            Array.Sort(intervals, 0, count);
            string row = string.Format(CultureInfo.InvariantCulture,
                "{0:O},{1},{2},{3},{4},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F1},{13},{14},{15},{16:F1},{17:F3},{18:F3},{19},{20}\n",
                DateTime.UtcNow, Game1.currentLocation?.Name ?? "Title", Game1.activeClickableMenu?.GetType().Name ?? "none",
                VerticalWorldRenderer.TwoPass && windowMode == LowerScreenMode.SceneHud ? 2 : 1, frames, frames * 1000.0 / elapsed,
                intervals[(count - 1) / 2], intervals[(int)((count - 1) * .95)],
                updateMs / Math.Max(1, updates), drawMs / frames, worldMs / frames, presentMs / frames,
                GC.GetTotalMemory(false) / 1048576.0, GC.CollectionCount(0) - gc0, TargetAllocations,
                activeFrames, GameRunner.instance?.InactiveSleepTime.TotalMilliseconds ?? 0,
                windowZoom, windowUiScale, mixedContext ? 1 : 0, windowMode);
            try { File.AppendAllText(path, row); } catch { }
            windowStart = frameStart;
            frames = updates = count = TargetAllocations = 0;
            activeFrames = 0;
            mixedContext = false;
            updateMs = drawMs = worldMs = presentMs = 0;
            gc0 = GC.CollectionCount(0);
        }
    }
}
