using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Enums;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    // Draw over the dual-screen output while the game or save loader yields
    // between stages. No background task touches the graphics device.
    internal static class LoadingOverlay
    {
        private static bool visible;
        private static bool loadingSave;
        private static string status = "正在启动游戏组件";
        private static float progress;
        private static long stageStarted;
        private static long titleScreenStarted;
        private static LoadStage stage = LoadStage.None;
        private static bool stageReported;
        private static SpriteBatch bootstrapBatch;
        private static Texture2D bootstrapPixel;
        private static GraphicsDevice bootstrapDevice;
        private const long StartupTitleSettleMilliseconds = 2500;

        internal static void Initialize(IModHelper helper)
        {
            if (Environment.GetEnvironmentVariable("SDV_DUAL_SCREEN_VERTICAL_WORLD") != "1") return;
            Set(false, "正在启动游戏组件", .04f);
            helper.Events.GameLoop.GameLaunched += (_, _) => Set(false, "正在准备游戏界面", .12f);
            helper.Events.Specialized.LoadStageChanged += (_, e) => OnLoadStage(e.NewStage);
            helper.Events.GameLoop.SaveLoaded += (_, _) => OnSaveLoaded();
            helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
            {
                stage = LoadStage.None;
                stageReported = false;
                titleScreenStarted = 0;
                Set(false, "正在返回标题画面", .08f);
            };
            helper.Events.GameLoop.UpdateTicked += (_, _) => UpdateProgress();
        }

        private static void Set(bool save, string message, float value)
        {
            visible = true;
            loadingSave = save;
            status = message;
            progress = MathHelper.Clamp(value, .02f, .96f);
            stageStarted = Environment.TickCount64;
        }

        private static void OnLoadStage(LoadStage next)
        {
            stage = next;
            stageReported = true;
            progress = ProgressForStage(next);
            switch (next)
            {
                case LoadStage.None:
                    if (!Context.IsWorldReady) Set(false, "正在进入游戏", .10f);
                    break;
                case LoadStage.CreatedBasicInfo: Set(false, "正在创建新游戏", .20f); break;
                case LoadStage.CreatedInitialLocations: Set(false, "正在准备游戏地图", .38f); break;
                case LoadStage.CreatedLocations: Set(false, "正在载入游戏内容", .56f); break;
                case LoadStage.CreatedSaveFile: Set(false, "正在建立存档", .76f); break;
                case LoadStage.SaveParsed: Set(true, "正在读取存档数据", .20f); break;
                case LoadStage.SaveLoadedBasicInfo: Set(true, "正在读取农场信息", .42f); break;
                case LoadStage.SaveAddedLocations: Set(true, "正在恢复游戏地图", .62f); break;
                case LoadStage.SaveLoadedLocations: Set(true, "正在恢复地图内容", .78f); break;
                case LoadStage.Preloaded: Set(true, "正在准备游戏界面", .90f); break;
                case LoadStage.Loaded:
                case LoadStage.Ready:
                    // SMAPI reaches these stages before SaveLoaded subscribers
                    // have finished; keep the overlay up through that event.
                    if (loadingSave) break;
                    break;
                case LoadStage.ReturningToTitle: Hide(); break;
            }
        }

        internal static bool ShouldKeepSaveOverlay(LoadStage value, bool saveLoading) =>
            saveLoading && value is LoadStage.Loaded or LoadStage.Ready;

        internal static bool ShouldHideForWorldReady(bool worldReady, bool saveLoading,
            bool hasLoadStage, LoadStage value) =>
            worldReady && !saveLoading && (!hasLoadStage || value is LoadStage.Loaded or LoadStage.Ready);

        internal static bool StartupTitleSettled(long titleShownAt, long now) =>
            titleShownAt != 0 && now - titleShownAt >= StartupTitleSettleMilliseconds;

        internal static float ProgressForStage(LoadStage value) => value switch
        {
            LoadStage.None => .10f,
            LoadStage.CreatedBasicInfo => .20f,
            LoadStage.CreatedInitialLocations => .38f,
            LoadStage.CreatedLocations => .56f,
            LoadStage.CreatedSaveFile => .76f,
            LoadStage.SaveParsed => .20f,
            LoadStage.SaveLoadedBasicInfo => .42f,
            LoadStage.SaveAddedLocations => .62f,
            LoadStage.SaveLoadedLocations => .78f,
            LoadStage.Preloaded => .90f,
            LoadStage.Loaded => .96f,
            LoadStage.Ready => 1f,
            _ => 0f
        };

        internal static void BeginTest(bool save)
        {
            stage = save ? LoadStage.SaveParsed : LoadStage.CreatedLocations;
            stageReported = true;
            Set(save, save ? "正在读取存档数据" : "正在载入游戏内容", save ? .42f : .56f);
        }

        internal static void EndTest() => Hide();

        private static void OnSaveLoaded()
        {
            titleScreenStarted = 0;
            Hide();
        }

        internal static object Status() => new { visible, loadingSave, status, progress };

        // GameRunner.Draw is the first reliable backbuffer hook. Draw here as
        // well as in the composed dual-screen frame so the pre-title/SMAPI
        // black-screen interval still shows a live loading indicator.
        internal static void DrawBootstrap(GraphicsDevice device)
        {
            if (!visible || device == null) return;
            if (bootstrapDevice != device || bootstrapBatch == null || bootstrapPixel == null
                || bootstrapPixel.IsDisposed)
            {
                bootstrapBatch?.Dispose();
                bootstrapPixel?.Dispose();
                bootstrapDevice = device;
                bootstrapBatch = new SpriteBatch(device);
                bootstrapPixel = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
                bootstrapPixel.SetData(new[] { Color.White });
            }

            int width = Math.Max(1, device.PresentationParameters.BackBufferWidth);
            int height = Math.Max(1, device.PresentationParameters.BackBufferHeight);
            bootstrapBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            bootstrapBatch.Draw(bootstrapPixel, new Rectangle(0, 0, width, height), new Color(9, 12, 20, 250));
            if (width >= 1600)
            {
                DrawBootstrapPanel(bootstrapBatch, new Rectangle(0, 0, width / 2, height));
                DrawBootstrapPanel(bootstrapBatch, new Rectangle(width / 2, 0, width - width / 2, height));
            }
            else
            {
                DrawBootstrapPanel(bootstrapBatch, new Rectangle(0, 0, width, height));
            }
            bootstrapBatch.End();
        }

        private static void DrawBootstrapPanel(SpriteBatch batch, Rectangle panel)
        {
            int cardWidth = Math.Min(panel.Width - 48, 860);
            int cardHeight = Math.Min(panel.Height - 48, 252);
            var card = new Rectangle(panel.Center.X - cardWidth / 2,
                panel.Center.Y - cardHeight / 2, cardWidth, cardHeight);
            batch.Draw(bootstrapPixel, card, new Color(33, 40, 55));
            var track = new Rectangle(card.X + 36, card.Bottom - 34, Math.Max(8, card.Width - 72), 16);
            batch.Draw(bootstrapPixel, track, new Color(13, 16, 24));
            batch.Draw(bootstrapPixel, new Rectangle(track.X, track.Y,
                Math.Max(1, (int)(track.Width * Math.Clamp(progress, .02f, .96f))), track.Height),
                new Color(82, 170, 220));
            if (Game1.smallFont == null) return;
            bool chinese = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh;
            int dots = (int)(Environment.TickCount64 / 450 % 4);
            string suffix = new string('.', dots);
            DrawCentered(batch, chinese
                ? loadingSave ? "正在加载存档" : "正在进入游戏"
                : loadingSave ? "Loading save" : "Entering game", panel.Center.X, card.Y + 40, Color.White);
            DrawCentered(batch, DisplayStatus(chinese) + suffix, panel.Center.X, card.Y + 92,
                new Color(227, 232, 241));
            DrawCentered(batch, chinese ? "请稍候，游戏仍在加载中" : "Please wait; the game is still loading",
                panel.Center.X, card.Y + 142, new Color(183, 194, 211));
            DrawCentered(batch, (chinese ? "进度 " : "Progress ")
                + Math.Clamp((int)Math.Round(progress * 100), 1, 99) + "%",
                panel.Center.X, card.Y + 178, new Color(183, 194, 211));
        }

        private static void UpdateProgress()
        {
            if (!visible) return;
            if (ShouldHideForWorldReady(Context.IsWorldReady, loadingSave, stageReported, stage))
            {
                Hide();
                return;
            }
            // The stage has no total item count. Move slowly instead of implying
            // a fabricated ETA, and hold below complete until SMAPI says Ready.
            float dwell = (Environment.TickCount64 - stageStarted) / 1000f;
            progress = Math.Min(.94f, progress + Math.Min(.002f, dwell * .00003f));
            if (loadingSave || Game1.activeClickableMenu is not TitleMenu)
            {
                titleScreenStarted = 0;
                return;
            }
            if (titleScreenStarted == 0)
                titleScreenStarted = Environment.TickCount64;
            else if (StartupTitleSettled(titleScreenStarted, Environment.TickCount64))
                Hide();
        }

        private static void Hide() => visible = false;

        internal static void Draw(SpriteBatch batch)
        {
            if (!visible || !VerticalWorldRenderer.Enabled || Game1.staminaRect == null || Game1.smallFont == null) return;
            int dots = (int)(Environment.TickCount64 / 450 % 4);
            string suffix = new string('.', dots);
            DrawPanel(batch, VerticalWorldRenderer.PhysicalUpper, suffix);
            DrawPanel(batch, VerticalWorldRenderer.PhysicalLower, suffix);
        }

        private static void DrawPanel(SpriteBatch batch, Rectangle panel, string suffix)
        {
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            batch.Draw(Game1.staminaRect, panel, new Color(9, 12, 20, 250));
            var card = new Rectangle(panel.X + 84, panel.Y + 236, panel.Width - 168, 252);
            batch.Draw(Game1.staminaRect, card, new Color(33, 40, 55));
            bool chinese = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh;
            DrawCentered(batch, chinese
                ? loadingSave ? "正在加载存档" : "正在进入游戏"
                : loadingSave ? "Loading save" : "Entering game", panel.Center.X, card.Y + 40, Color.White);
            DrawCentered(batch, DisplayStatus(chinese) + suffix, panel.Center.X, card.Y + 92, new Color(227, 232, 241));
            DrawCentered(batch, chinese
                ? "请稍候，游戏仍在加载中"
                : "Please wait; the game is still loading", panel.Center.X, card.Y + 142, new Color(183, 194, 211));
            DrawCentered(batch, (chinese ? "进度 " : "Progress ") + Math.Clamp((int)Math.Round(progress * 100), 1, 99) + "%",
                panel.Center.X, card.Y + 178, new Color(183, 194, 211));
            var track = new Rectangle(card.X + 36, card.Bottom - 34, card.Width - 72, 16);
            batch.Draw(Game1.staminaRect, track, new Color(13, 16, 24));
            batch.Draw(Game1.staminaRect, new Rectangle(track.X, track.Y,
                Math.Max(1, (int)(track.Width * progress)), track.Height), new Color(82, 170, 220));
            batch.End();
        }

        private static string DisplayStatus(bool chinese)
        {
            if (chinese) return status;
            return stage switch
            {
                LoadStage.None => "Starting game",
                LoadStage.CreatedBasicInfo => "Creating new game",
                LoadStage.CreatedInitialLocations => "Preparing farm map",
                LoadStage.CreatedLocations => "Loading game content",
                LoadStage.CreatedSaveFile => "Writing save file",
                LoadStage.SaveParsed => "Reading save data",
                LoadStage.SaveLoadedBasicInfo => "Loading farm information",
                LoadStage.SaveAddedLocations => "Restoring locations",
                LoadStage.SaveLoadedLocations => "Loading map contents",
                LoadStage.Preloaded => "Finalizing game setup",
                _ => loadingSave ? "Loading save data" : "Loading game data"
            };
        }

        private static void DrawCentered(SpriteBatch batch, string text, int centerX, int y, Color color)
        {
            const float scale = 1.15f;
            Vector2 size = Game1.smallFont.MeasureString(text) * scale;
            batch.DrawString(Game1.smallFont, text, new Vector2(centerX - size.X / 2, y),
                color, 0, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }
    }
}
