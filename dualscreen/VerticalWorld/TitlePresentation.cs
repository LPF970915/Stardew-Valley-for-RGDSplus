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
        private static Texture2D skyTexture;
        private static SpriteFont attributionFont;
        private static readonly Rectangle[] cloudSources =
        {
            new Rectangle(152, 447, 123, 55), new Rectangle(0, 471, 149, 66), new Rectangle(410, 467, 63, 37)
        };
        private static readonly Point[] clouds =
        {
            new Point(80, 145), new Point(675, 120),
            new Point(160, 590), new Point(710, 625),
            new Point(395, 715), new Point(880, 730),
            new Point(60, 845), new Point(555, 870),
            new Point(280, 965), new Point(780, 990),
            new Point(95, 1100), new Point(620, 1080),
            new Point(430, 660), new Point(930, 930), new Point(470, 1180)
        };

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
            if (__instance.muteMusicButton != null) __instance.muteMusicButton.visible = false;
            if (__instance.windowedButton != null) __instance.windowedButton.visible = false;
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
            if (texture == Game1.mouseCursors && r.Y == 384 && (r.X == 128 || r.X == 137 || r.X == 146 || r.X == 155)) return true;
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

            DrawGradient(b, bounds, sceneTop, sceneShift, scale);
            DrawStars(b, bounds, sceneTop, sceneShift, scale);
            DrawClouds(b, title, bounds, sceneTop, sceneShift, scale);
            DrawMountains(b, bounds, sceneTop, sceneShift, scale);
            if (sceneTop == 0) DrawLogo(b, title, bounds, sceneTop, sceneShift, scale);
            else if (TitleMenu.subMenu == null) DrawAttribution(b, bounds);

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

        private static void DrawGradient(SpriteBatch b, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            if (skyTexture == null || skyTexture.IsDisposed || skyTexture.GraphicsDevice != b.GraphicsDevice)
            {
                skyTexture?.Dispose();
                skyTexture = new Texture2D(b.GraphicsDevice, 1, SceneHeight, false, SurfaceFormat.Color);
                var pixels = new Color[SceneHeight];
                for (int y = 0; y < pixels.Length; y++) pixels[y] = SkyColor(y / (float)(SceneHeight - 1));
                skyTexture.SetData(pixels);
            }

            b.Draw(Game1.staminaRect, bounds, SkyColor(MathHelper.Clamp((sceneTop - shift) / SceneHeight, 0f, 1f)));
            var destination = new Rectangle(bounds.X, bounds.Y + (int)((shift - sceneTop) * scale), bounds.Width, (int)(SceneHeight * scale));
            DrawClipped(b, skyTexture, destination, new Rectangle(0, 0, 1, SceneHeight), bounds);
        }

        private static Color SkyColor(float t)
        {
            if (t < 0.48f) return Color.Lerp(new Color(19, 55, 148), new Color(56, 143, 238), t / 0.48f);
            return Color.Lerp(new Color(56, 143, 238), new Color(154, 220, 255), (t - 0.48f) / 0.52f);
        }

        private static void DrawStars(SpriteBatch b, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            if (sceneTop != 0) return;
            // Reuse TitleMenu.draw's starfield, including its translucent halos
            // and four-point stars, at 2x so it fits above the dual-screen logo.
            var source = new Rectangle(0, 1453, 638, 195);
            int width = (int)(source.Width * 2 * scale);
            var destination = new Rectangle(bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (int)(shift * scale), width, (int)(source.Height * 2 * scale));
            DrawClipped(b, Game1.mouseCursors, destination, source, bounds);
        }

        private static void DrawClouds(SpriteBatch b, TitleMenu title, Rectangle bounds, int sceneTop, float shift, float scale)
        {
            if (title.cloudsTexture == null) return;
            double seconds = Game1.currentGameTime?.TotalGameTime.TotalSeconds ?? 0;
            for (int i = 0; i < clouds.Length; i++)
            {
                Rectangle source = cloudSources[i % cloudSources.Length];
                int cloudW = Math.Max(1, (int)(source.Width * scale));
                int cloudH = Math.Max(1, (int)(source.Height * scale));
                float speed = 12f + i % 3 * 5f;
                float x = (float)((seconds * speed + clouds[i].X + source.Width) % (SceneWidth + source.Width)) - source.Width;
                float y = bounds.Y + (clouds[i].Y - sceneTop + shift) * scale;
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
            int x = bounds.X + (bounds.Width - (int)(LogoWidth * scale)) / 2;
            int centeredY = bounds.Y + (bounds.Height - (int)(LogoHeight * scale)) / 2;
            int y = centeredY + (int)((eased - 1f) * bounds.Height * 0.55f);
            DrawClipped(b, title.titleButtonsTexture, new Rectangle(x, y, (int)(LogoWidth * scale), (int)(LogoHeight * scale)), new Rectangle(0, 0, 400, 187), bounds);
        }

        private static void DrawAttribution(SpriteBatch b, Rectangle bounds)
        {
            // The credit stays Chinese even when the game uses English. Load
            // its Chinese font explicitly instead of using the active-language font.
            var font = attributionFont ??= Game1.content.Load<SpriteFont>("Fonts/SmallFont.zh-CN");
            string[] lines = { "Stardew Valley " + Game1.version + " for RGDSplus", "移植by Blood_roc" };
            float panelScale = bounds.Width / 1024f;
            float scale = 32f * panelScale / font.LineSpacing;
            float y = bounds.Y + 18f * panelScale;
            for (int i = 0; i < lines.Length; i++)
            {
                var size = font.MeasureString(lines[i]) * scale;
                var position = new Vector2((float)Math.Round(bounds.Right - 22f * panelScale - size.X), (float)Math.Round(y));
                b.DrawString(font, lines[i], position + new Vector2(2f * panelScale), new Color(18, 49, 103) * 0.9f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.89f);
                b.DrawString(font, lines[i], position, new Color(243, 248, 255), 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9f);
                y += 36f * panelScale;
            }
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
