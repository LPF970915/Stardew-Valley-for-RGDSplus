using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class TitlePresentation
    {
        private const int SceneWidth = 1024;
        private const int SceneHeight = 1536;
        private const int PanelHeight = 768;
        private const int LogoWidth = 800;
        private const int LogoHeight = 374;

        [ThreadStatic] private static TitleMenu drawingTitle;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TitleMenu), "draw", new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(TitlePresentation), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(TitlePresentation), nameof(AfterDraw)),
                transpiler: new HarmonyMethod(typeof(TitlePresentation), nameof(RouteTitleDraws)));
        }

        private static void BeforeDraw(TitleMenu __instance, SpriteBatch b)
        {
            drawingTitle = __instance;
            var bounds = new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
            DrawSceneSlice(b, __instance, bounds, PanelHeight);
        }

        private static Exception AfterDraw(Exception __exception)
        {
            drawingTitle = null;
            return __exception;
        }

        internal static IEnumerable<CodeInstruction> RouteTitleDraws(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                if (code.operand is MethodInfo method && method.DeclaringType == typeof(SpriteBatch) && method.Name == "Draw")
                {
                    var parameters = method.GetParameters();
                    var signature = new Type[parameters.Length + 1];
                    signature[0] = typeof(SpriteBatch);
                    for (int i = 0; i < parameters.Length; i++) signature[i + 1] = parameters[i].ParameterType;
                    var wrapper = typeof(TitlePresentation).GetMethod(nameof(Draw), BindingFlags.Static | BindingFlags.NonPublic, null, signature, null);
                    if (wrapper != null)
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = wrapper;
                    }
                }
                yield return code;
            }
        }

        private static bool Suppress(Texture2D texture, Rectangle? source, Color color)
        {
            if (!VerticalWorldRenderer.Enabled || drawingTitle == null) return false;
            if (texture == drawingTitle.cloudsTexture) return true;
            if (texture == Game1.staminaRect && color == new Color(64, 136, 248)) return true;
            if (!source.HasValue) return false;

            var r = source.Value;
            if (texture == drawingTitle.titleButtonsTexture && r == new Rectangle(0, 0, 400, 187)) return true;
            if (texture == Game1.mouseCursors && (r.Y == 1912 || r.Y == 1453 || r.Y == 886 || r.Y == 737)) return true;
            return false;
        }

        private static void Draw(SpriteBatch b, Texture2D texture, Rectangle destination, Color color)
        {
            if (!Suppress(texture, null, color)) b.Draw(texture, destination, color);
        }

        private static void Draw(SpriteBatch b, Texture2D texture, Rectangle destination, Rectangle? source, Color color)
        {
            if (!Suppress(texture, source, color)) b.Draw(texture, destination, source, color);
        }

        private static void Draw(SpriteBatch b, Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
        {
            if (!Suppress(texture, source, color)) b.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
        }

        internal static void DrawUpper(SpriteBatch b, TitleMenu title, Rectangle bounds)
        {
            DrawSceneSlice(b, title, bounds, 0);
        }

        private static void DrawSceneSlice(SpriteBatch b, TitleMenu title, Rectangle bounds, int sceneTop)
        {
            if (title == null || bounds.Width <= 0 || bounds.Height <= 0) return;

            float scale = bounds.Width / (float)SceneWidth;
            float sceneShift = GetSceneShift(title);

            DrawGradient(b, bounds, sceneTop, sceneShift);
            DrawStars(b, bounds, sceneTop, sceneShift, scale);
            DrawClouds(b, title, bounds, sceneTop, sceneShift, scale);
            DrawMountains(b, bounds, sceneTop, sceneShift, scale);
            DrawLogo(b, title, bounds, sceneTop, sceneShift, scale);

            if (title.logoFadeTimer > 0)
            {
                b.Draw(Game1.staminaRect, bounds, Color.White);
            }
            else if (title.fadeFromWhiteTimer > 0)
            {
                float alpha = MathHelper.Clamp(title.fadeFromWhiteTimer / 2000f, 0f, 1f);
                b.Draw(Game1.staminaRect, bounds, Color.White * alpha);
            }
        }

        private static float GetSceneShift(TitleMenu title)
        {
            float progress = MathHelper.Clamp(-title.viewportY / 1000f, 0f, 1f);
            return 220f * (1f - progress);
        }

        private static void DrawGradient(SpriteBatch b, Rectangle bounds, int sceneTop, float shift)
        {
            const int bands = 24;
            for (int i = 0; i < bands; i++)
            {
                int y0 = bounds.Y + i * bounds.Height / bands;
                int y1 = bounds.Y + (i + 1) * bounds.Height / bands;
                int worldY = sceneTop + (int)((y0 - bounds.Y) / (float)bounds.Height * PanelHeight - shift);
                float t = MathHelper.Clamp(worldY / (float)SceneHeight, 0f, 1f);
                b.Draw(Game1.staminaRect, new Rectangle(bounds.X, y0, bounds.Width, Math.Max(1, y1 - y0 + 1)), SkyColor(t));
            }
        }

        private static Color SkyColor(float t)
        {
            if (t < 0.48f) return Color.Lerp(new Color(19, 55, 148), new Color(56, 143, 238), t / 0.48f);
            return Color.Lerp(new Color(56, 143, 238), new Color(154, 220, 255), (t - 0.48f) / 0.52f);
        }

        private static void DrawStars(SpriteBatch b, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            int[] stars = { 90, 130, 214, 282, 356, 433, 514, 612, 694, 786, 864, 946 };
            for (int i = 0; i < stars.Length; i++)
            {
                float worldY = 100 + (i * 47 % 285);
                float screenY = bounds.Y + worldY - sceneTop + shift;
                if (screenY < bounds.Y - 4 || screenY > bounds.Bottom + 4) continue;
                int size = i % 3 == 0 ? 3 : 2;
                Color color = i % 4 == 0 ? new Color(255, 242, 180) : Color.White;
                b.Draw(Game1.staminaRect, new Rectangle(bounds.X + (int)(stars[i] * scale), (int)screenY, size, size), color * 0.9f);
            }
        }

        private static void DrawClouds(SpriteBatch b, TitleMenu title, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            if (title.cloudsTexture == null) return;
            double seconds = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
            Rectangle[] sources =
            {
                new Rectangle(152, 447, 123, 55), new Rectangle(0, 471, 149, 66), new Rectangle(410, 467, 63, 37),
                new Rectangle(152, 447, 123, 55), new Rectangle(0, 471, 149, 66), new Rectangle(410, 467, 63, 37)
            };
            int[] worldYs = { 210, 355, 500, 650, 810, 970 };
            for (int i = 0; i < sources.Length; i++)
            {
                Rectangle source = sources[i];
                int cloudW = Math.Max(1, (int)(source.Width * scale));
                int cloudH = Math.Max(1, (int)(source.Height * scale));
                float speed = 12f + i % 3 * 5f;
                float x = (float)((seconds * speed + i * 191) % (SceneWidth + cloudW)) - cloudW;
                float y = bounds.Y + worldYs[i] - sceneTop + shift;
                DrawClipped(b, title.cloudsTexture, new Rectangle(bounds.X + (int)(x * scale), (int)y, cloudW, cloudH), source, bounds);
            }
        }

        private static void DrawMountains(SpriteBatch b, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            DrawWorldTexture(b, Game1.mouseCursors, bounds, sceneTop, shift, scale, new Rectangle(0, 886, 639, 148), 1190, 250, new Color(201, 226, 244));
            DrawWorldTexture(b, Game1.mouseCursors, bounds, sceneTop, shift, scale, new Rectangle(0, 737, 639, 148), 1305, 290, Color.White);
        }

        private static void DrawWorldTexture(SpriteBatch b, Texture2D texture, Rectangle bounds, int sceneTop, float shift, float scale, Rectangle source, int worldY, int height, Color color)
        {
            var destination = new Rectangle(bounds.X, bounds.Y + (int)((worldY - sceneTop + shift) * scale), bounds.Width, (int)(height * scale));
            DrawClipped(b, texture, destination, source, bounds, color);
        }

        private static void DrawLogo(SpriteBatch b, TitleMenu title, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            float progress;
            if (title.logoSwipeTimer > 0f) progress = 1f - MathHelper.Clamp(title.logoSwipeTimer / 1000f, 0f, 1f);
            else if (title.titleInPosition) progress = 1f;
            else return;

            float p = MathHelper.Clamp(progress, 0f, 1f);
            float eased = 1f + 2.2f * (p - 1f) * (p - 1f) * (p - 1f) + 1.2f * (p - 1f) * (p - 1f);
            float worldY = -260f + 520f * eased;
            int x = bounds.X + (bounds.Width - (int)(LogoWidth * scale)) / 2;
            int y = bounds.Y + (int)((worldY - sceneTop + shift) * scale);
            DrawClipped(b, title.titleButtonsTexture, new Rectangle(x, y, (int)(LogoWidth * scale), (int)(LogoHeight * scale)), new Rectangle(0, 0, 400, 187), bounds);
        }

        private static void DrawClipped(SpriteBatch b, Texture2D texture, Rectangle destination, Rectangle source, Rectangle clip, Color color = default(Color))
        {
            if (texture == null) return;
            if (color == default(Color)) color = Color.White;
            var visible = Rectangle.Intersect(destination, clip);
            if (visible.Width <= 0 || visible.Height <= 0) return;
            var crop = new Rectangle(source.X + (visible.X - destination.X) * source.Width / Math.Max(1, destination.Width),
                source.Y + (visible.Y - destination.Y) * source.Height / Math.Max(1, destination.Height),
                Math.Max(1, visible.Width * source.Width / Math.Max(1, destination.Width)),
                Math.Max(1, visible.Height * source.Height / Math.Max(1, destination.Height)));
            b.Draw(texture, visible, crop, color);
        }
    }
}
