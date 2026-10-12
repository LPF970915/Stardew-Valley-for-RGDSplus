using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    // Discard transparent texels before blending, retaining native sprite
    // coordinates, resolution, colors, sort order and partial transparency.
    internal static class WorldSpriteEffect
    {
        private static AlphaTestEffect effect;
        private static bool unavailable;
        internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("SDV_WORLD_ALPHA_DISCARD") == "1";
        internal static bool IsStandard(Effect value) => value == null || ReferenceEquals(value, effect);

        internal static void Install(Harmony harmony)
        {
            if (Enabled) harmony.Patch(AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Begin)),
                prefix: new HarmonyMethod(typeof(WorldSpriteEffect), nameof(BeforeBegin)));
        }

        private static void BeforeBegin(SpriteBatch __instance, BlendState blendState, ref Effect effect, Matrix? transformMatrix)
        {
            if (unavailable || !VerticalWorldRenderer.DrawingWorld || effect != null || __instance != Game1.spriteBatch
                || SpriteBatch.globalMatrix.HasValue
                || blendState != null && blendState != BlendState.AlphaBlend) return;
            var device = __instance.GraphicsDevice;
            if (WorldSpriteEffect.effect == null || WorldSpriteEffect.effect.IsDisposed || WorldSpriteEffect.effect.GraphicsDevice != device)
            {
                try
                {
                    WorldSpriteEffect.effect?.Dispose();
                    WorldSpriteEffect.effect = new AlphaTestEffect(device)
                    { VertexColorEnabled = true, AlphaFunction = CompareFunction.Greater, ReferenceAlpha = 0 };
                }
                catch (Exception error)
                {
                    unavailable = true;
                    ModEntry.Write("world-alpha fallback: " + error.Message);
                    return;
                }
            }
            var shader = WorldSpriteEffect.effect;
            var viewport = device.Viewport;
            var projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, -1);
            if (device.UseHalfPixelOffset)
            {
                projection.M41 += -.5f * projection.M11;
                projection.M42 += -.5f * projection.M22;
            }
            shader.World = transformMatrix ?? Matrix.Identity;
            shader.View = Matrix.Identity;
            shader.Projection = projection;
            effect = shader;
        }

        internal static void Release()
        {
            effect?.Dispose();
            effect = null;
        }
    }
}
