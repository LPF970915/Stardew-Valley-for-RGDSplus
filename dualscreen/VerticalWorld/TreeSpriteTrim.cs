using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Rgdsplus.VerticalWorld
{
    // Reduce canopy quad area without changing source pixels. Moving/rotating
    // trees, custom effects and non-point sampling retain the original draw.
    internal static class TreeSpriteTrim
    {
        internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("SDV_TREE_TRIM") == "1";
        private sealed class TextureCache { public TextureCache() {} internal readonly Dictionary<Rectangle, Rectangle[]> Regions = new(); internal bool Failed; }
        private static ConditionalWeakTable<Texture2D, TextureCache> cache = new();
        private static Texture2D treeTexture;
        private static bool eligibleBatch, splitting;
        internal static int StripHeight = 32;

        internal static void Install(Harmony harmony)
        {
            if (!Enabled) return;
            harmony.Patch(AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Begin)),
                prefix: new HarmonyMethod(typeof(TreeSpriteTrim), nameof(BatchBegin)));
            harmony.Patch(AccessTools.Method(typeof(Tree), nameof(Tree.draw), new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(TreeSpriteTrim), nameof(TreeBegin)),
                finalizer: new HarmonyMethod(typeof(TreeSpriteTrim), nameof(TreeEnd)));
            harmony.Patch(AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] {
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float),
                typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float) }),
                prefix: new HarmonyMethod(typeof(TreeSpriteTrim), nameof(Draw)));
        }

        private static void BatchBegin(SpriteBatch __instance, BlendState blendState, SamplerState samplerState, Effect effect, Matrix? transformMatrix)
        {
            if (__instance == Game1.spriteBatch)
                eligibleBatch = VerticalWorldRenderer.DrawingWorld && WorldRenderQuality.Scale == 1f
                    && samplerState == SamplerState.PointClamp && WorldSpriteEffect.IsStandard(effect)
                    && !SpriteBatch.globalMatrix.HasValue && (!transformMatrix.HasValue || transformMatrix.Value == Matrix.Identity)
                    && (blendState == null || blendState == BlendState.AlphaBlend);
        }
        private static void TreeBegin(Tree __instance, out Texture2D __state)
        {
            __state = treeTexture;
            treeTexture = eligibleBatch && __instance.texture.IsValueCreated ? __instance.texture.Value : null;
        }
        private static void TreeEnd(Texture2D __state) { treeTexture = __state; }

        internal static Rectangle[] FindStrips(uint[] pixels, int width, Rectangle source)
        {
            var strips = new List<Rectangle>(96 / StripHeight);
            int area = 0;
            for (int y = source.Top; y < source.Bottom; y += StripHeight)
            {
                int left = source.Right, right = source.Left, top = Math.Min(y+StripHeight, source.Bottom), bottom = y;
                for (int yy = y; yy < Math.Min(y+StripHeight, source.Bottom); yy++)
                for (int x = source.Left; x < source.Right; x++)
                    // Preserve even non-premultiplied RGB under zero alpha.
                    if (pixels[yy*width+x] != 0)
                    { left=Math.Min(left,x); right=Math.Max(right,x+1); top=Math.Min(top,yy); bottom=Math.Max(bottom,yy+1); }
                if (right > left)
                {
                    var strip = new Rectangle(left, top, right-left, bottom-top);
                    strips.Add(strip); area += strip.Width*strip.Height;
                }
            }
            return area < source.Width*source.Height*.8 ? strips.ToArray() : new[] { source };
        }

        private static bool Draw(SpriteBatch __instance, Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
            Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
        {
            if (splitting || !eligibleBatch || __instance != Game1.spriteBatch || texture == null || texture != treeTexture
                || rotation != 0f || !sourceRectangle.HasValue || !VerticalWorldRenderer.DrawingWorld
                || scale != new Vector2(4f) || origin != new Vector2(24f,96f)
                || position.X != MathF.Floor(position.X) || position.Y != MathF.Floor(position.Y)
                || SpriteBatch.TextureTuckAmount != .001f) return true;
            var source = sourceRectangle.Value;
            if (source.Width != 48 || source.Height != 96 || source.X < 0 || source.Y < 0
                || source.Right > texture.Width || source.Bottom > texture.Height) return true;
            var data = cache.GetOrCreateValue(texture);
            if (data.Failed) return true;
            if (!data.Regions.TryGetValue(source, out var strips))
            {
                try
                {
                    var pixels = new uint[texture.Width * texture.Height];
                    texture.GetData(pixels);
                    strips = FindStrips(pixels, texture.Width, source);
                    data.Regions.Add(source, strips);
                }
                catch (Exception error)
                {
                    data.Failed = true;
                    ModEntry.Write("tree-trim fallback: " + error.Message);
                    return true;
                }
            }
            if (strips.Length == 1 && strips[0] == source) return true;
            splitting = true;
            try
            {
                foreach (var strip in strips)
                {
                    int x = strip.X-source.X, y = strip.Y-source.Y;
                    if ((effects & SpriteEffects.FlipHorizontally) != 0) x = source.Width-x-strip.Width;
                    if ((effects & SpriteEffects.FlipVertically) != 0) y = source.Height-y-strip.Height;
                    __instance.Draw(texture, position, strip, color, rotation, origin-new Vector2(x,y), scale, effects, layerDepth);
                }
            }
            finally { splitting = false; }
            return false;
        }

        internal static void Clear() { cache = new(); treeTexture = null; eligibleBatch = false; }
    }
}
