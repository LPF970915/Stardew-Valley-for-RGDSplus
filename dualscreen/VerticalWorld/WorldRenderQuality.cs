using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Rgdsplus.VerticalWorld
{
    // Raster resolution is independent of camera, gameplay, UI and touch coordinates.
    internal static class WorldRenderQuality
    {
        internal static float Scale { get; private set; } = RequestedScale();
        private static bool presentationConfigured;
        internal static int SwapInterval { get; private set; } = -1;
        private static readonly Dictionary<Texture2D, Point> logicalSizes = new();
        private static readonly AccessTools.FieldRef<GraphicsDevice, RenderTargetBinding[]> Bindings =
            AccessTools.FieldRefAccess<GraphicsDevice, RenderTargetBinding[]>("_currentRenderTargetBindings");

        [DllImport("libSDL2-2.0.so.0")]
        private static extern int SDL_GL_SetSwapInterval(int interval);
        [DllImport("libSDL2-2.0.so.0")]
        private static extern int SDL_GL_GetSwapInterval();

        private static float RequestedScale()
        {
            return float.TryParse(Environment.GetEnvironmentVariable("SDV_WORLD_RENDER_SCALE"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == .625f ? value : 1f;
        }

        internal static void Install(Harmony harmony)
        {
            if (Scale == 1f) return;
            harmony.Patch(AccessTools.PropertyGetter(typeof(GraphicsDevice), nameof(GraphicsDevice.Viewport)),
                postfix: new HarmonyMethod(typeof(WorldRenderQuality), nameof(LogicalViewport)));
        }

        internal static void ConfigurePresentation()
        {
            if (presentationConfigured) return;
            presentationConfigured = true;
            try
            {
                // Preserve synchronized presentation at native resolution.
                int result = SDL_GL_SetSwapInterval(1);
                SwapInterval = SDL_GL_GetSwapInterval();
                if (result != 0) Scale = 1f;
                ModEntry.Write("world-raster scale=" + Scale.ToString(CultureInfo.InvariantCulture) + "; swap-result=" + result
                    + "; swap-interval=" + SwapInterval + "; target-draw-rate=" + (FramePacing.Enabled ? 30 : 0));
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            {
                Scale = 1f;
                ModEntry.Write("world-raster native fallback: " + error.Message);
            }
        }

        internal static int Pixels(int logical) => Math.Max(1, (int)Math.Round(logical * Scale));

        internal static void Register(RenderTarget2D target, int width, int height)
        {
            if (Scale == 1f) return;
            if (logicalSizes.Count > 4)
            {
                var disposed = new List<Texture2D>();
                foreach (var old in logicalSizes.Keys) if (old.IsDisposed) disposed.Add(old);
                foreach (var old in disposed) logicalSizes.Remove(old);
            }
            logicalSizes[target] = new Point(width, height);
        }

        private static void LogicalViewport(GraphicsDevice __instance, ref Viewport __result)
        {
            if (__instance.RenderTargetCount == 0) return;
            var target = Bindings(__instance)[0].RenderTarget as Texture2D;
            if (target == null || !logicalSizes.TryGetValue(target, out var size)) return;
            __result.Width = size.X;
            __result.Height = size.Y;
        }

        internal static Rectangle Source(Texture2D texture, Rectangle logical)
        {
            if (!logicalSizes.TryGetValue(texture, out var size)) return logical;
            float sx = texture.Width / (float)size.X, sy = texture.Height / (float)size.Y;
            int x = (int)Math.Round(logical.Left * sx), y = (int)Math.Round(logical.Top * sy);
            return new Rectangle(x, y, (int)Math.Round(logical.Right * sx) - x, (int)Math.Round(logical.Bottom * sy) - y);
        }

        internal static void Clear() => logicalSizes.Clear();
    }
}
