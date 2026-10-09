using System;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    public sealed class ModEntry : Mod
    {
        internal static IMonitor Log;
        internal static string LogPath;
        public override void Entry(IModHelper helper)
        {
            Log = Monitor;
            LogPath = Path.Combine(helper.DirectoryPath, "vertical-world.log");
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
                File.WriteAllText(LogPath, "");
            VerticalWorldRenderer.Install();
            CompanionPreferences.Initialize(helper);
            CompanionHistory.Initialize(helper);
            FrameMetrics.Initialize(helper.DirectoryPath);
            DisplayModeController.Initialize(helper);
            UiTestDriver.Initialize(helper);
            helper.Events.GameLoop.ReturnedToTitle += (_, e) => VerticalWorldRenderer.ReleaseWorld();
            Write("loaded version=0.6.0-reference-v2; upper=" + (VerticalWorldRenderer.UpperOnRight ? "right" : "left") + "; world-passes=" + (VerticalWorldRenderer.TwoPass ? 2 : 1));
        }
        internal static void Write(string line)
        {
            try { File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine); }
            catch { }
        }
    }

    internal static class VerticalWorldRenderer
    {
        internal static bool Enabled { get; private set; }
        // Successful device build: upper=left (DRM plane 98), lower=right (plane 58).
        internal static readonly bool UpperOnRight = Environment.GetEnvironmentVariable("SDV_RGDS_PHYSICAL_UPPER") == "right";
        internal static readonly bool TwoPass = Environment.GetEnvironmentVariable("SDV_DUAL_WORLD_PASSES") == "2";
        internal static readonly bool LowerCachePrototype = Environment.GetEnvironmentVariable("SDV_LOWER_WORLD_CACHE") == "1";
        private static readonly int LowerCacheInterval = ParseCacheInterval();
        internal static Rectangle PhysicalUpper => new Rectangle(UpperOnRight ? 1024 : 0, 0, 1024, 768);
        internal static Rectangle PhysicalLower => new Rectangle(UpperOnRight ? 0 : 1024, 0, 1024, 768);
        internal static int WorldWidth => ScreenGeometry.Width(Game1.options?.zoomLevel ?? 1f);
        internal static int WorldHeight => ScreenGeometry.Height(Game1.options?.zoomLevel ?? 1f);
        internal static bool DrawingWorld;
        private static bool secondPass, worldThisFrame, cachedWorld, lowerCacheValid;
        private static int lowerCacheFrame = -1;
        private static RenderTarget2D world, lower, upperUi;
        internal static RenderTarget2D UpperUi => upperUi;
        private static bool upperUiThisFrame;
        private static bool NeedsUpperUi => DisplayModeController.Expanded && (Game1.dialogueUp || Game1.activeClickableMenu is DialogueBox);
        private static bool UseTwoPass => (TwoPass || LowerCachePrototype) && !DisplayModeController.Expanded;
        private static GameLocation lastLocation;
        private static int cameraX, cameraY;
        private static long lastGeometryLog;
        private static int presentationSamples;
        private static readonly Action<int, int> AllocateLightmap = (Action<int, int>)Delegate.CreateDelegate(typeof(Action<int, int>), AccessTools.Method(typeof(Game1), "allocateLightmap"));

        private static int ParseCacheInterval()
        {
            return int.TryParse(Environment.GetEnvironmentVariable("SDV_LOWER_CACHE_INTERVAL"), out var value)
                ? Math.Max(1, Math.Min(30, value)) : 4;
        }

        internal static void Install()
        {
            if (Environment.GetEnvironmentVariable("SDV_DUAL_SCREEN_VERTICAL_WORLD") != "1") return;
            var harmony = new Harmony("RGDSplus.VerticalWorld");
            try
            {
                Enabled = true;
                Patch(harmony, typeof(Game1), "Draw", nameof(DrawPrefix), nameof(DrawPostfix));
                Patch(harmony, typeof(Game1), "Update", nameof(UpdatePrefix), nameof(UpdatePostfix));
                Patch(harmony, typeof(Game1), "DrawWorld", nameof(WorldPrefix), nameof(WorldPostfix), nameof(WorldFinalizer));
                Patch(harmony, typeof(Game1), "ShouldDrawOnBuffer", nameof(BufferPrefix));
                Patch(harmony, typeof(Game1), "renderScreenBuffer", nameof(PresentPrefix));
                Patch(harmony, typeof(GameRunner), "Draw", postfix: nameof(RunnerDrawPostfix));
                UiPatches.Install(harmony);
                WeatherViewport.Install(harmony);
                TitlePresentation.Install(harmony);
                DialogueRouting.Install(harmony);
                ModEntry.Log?.Log("Reference v2 installed: Select toggles continuous world / persistent lower pages; companion Start blocked; upper dialogue routing enabled.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Enabled = false;
                harmony.UnpatchAll(harmony.Id);
                ModEntry.Log?.Log("Reference v2 installation failed; patches removed: " + ex, LogLevel.Error);
                ModEntry.Write("install-failed " + ex);
            }
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, string finalizer = null)
        {
            harmony.Patch(AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method),
                prefix == null ? null : new HarmonyMethod(typeof(VerticalWorldRenderer), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(VerticalWorldRenderer), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(VerticalWorldRenderer), finalizer));
        }

        private static void UpdatePrefix(Game1 __instance)
        {
            if (!Enabled || __instance.takingMapScreenshot) return;
            FrameMetrics.BeginUpdate();
            UiPatches.EnsureViewports();
        }
        private static void UpdatePostfix() { if (Enabled) FrameMetrics.EndUpdate(); }
        private static void DrawPrefix(Game1 __instance)
        {
            if (!Enabled || __instance.takingMapScreenshot) return;
            FrameMetrics.BeginFrame();
            worldThisFrame = false;
            EnsureTargets(__instance);
            upperUiThisFrame = NeedsUpperUi;
            if (upperUiThisFrame)
            {
                var device = __instance.GraphicsDevice;
                var targets = device.GetRenderTargets();
                var viewport = device.Viewport;
                device.SetRenderTarget(upperUi);
                device.Clear(Color.Transparent);
                device.SetRenderTargets(targets);
                device.Viewport = viewport;
            }
            UiPatches.EnsureViewports();
        }
        private static void DrawPostfix() { if (Enabled) FrameMetrics.EndFrame(); }
        private static void RunnerDrawPostfix(GameRunner __instance)
        {
            if (!Enabled) return;
            var pp = __instance.GraphicsDevice.PresentationParameters;
            __instance.GraphicsDevice.Viewport = new Viewport(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
        }
        private static bool BufferPrefix(Game1 __instance, ref bool __result)
        {
            if (!Enabled || __instance.takingMapScreenshot) return true;
            __result = true;
            return false;
        }

        internal sealed class WorldState
        {
            internal RenderTarget2D Destination;
            internal xTile.Dimensions.Rectangle Viewport;
            internal bool Active;
        }
        internal static bool IsSecondPass => secondPass;
        internal static xTile.Dimensions.Rectangle SceneViewport()
        {
            int w=WorldWidth,h=WorldHeight;
            var location=Game1.currentLocation;
            int x=Game1.viewport.X,y=Game1.viewport.Y;
            if(location?.Map!=null && Game1.player!=null && !Game1.eventUp && !Game1.viewportFreeze)
            {
                x=ScreenGeometry.CameraX(Game1.player.Position.X,location.Map.DisplayWidth,w);
                y=ScreenGeometry.CameraY(Game1.player.Position.Y,location.Map.DisplayHeight,h,!DisplayModeController.Expanded);
            }
            return new xTile.Dimensions.Rectangle(x,y,w,DisplayModeController.Expanded?h:h*2);
        }
        private static void WorldPrefix(Game1 __instance, ref RenderTarget2D target_screen, out WorldState __state)
        {
            __state = null;
            if (!Enabled || secondPass || __instance.takingMapScreenshot || Game1.currentLocation == null) return;
            EnsureTargets(__instance);
            int w = WorldWidth, h = WorldHeight;
            bool expanded = DisplayModeController.Expanded;
            Resize(ref world, __instance.GraphicsDevice, w, expanded || UseTwoPass ? h : h * 2, "Reference.World");
            if (UseTwoPass) Resize(ref lower, __instance.GraphicsDevice, w, h, "Reference.Lower");
            var location = Game1.currentLocation;
            int mapW = location.Map.DisplayWidth, mapH = location.Map.DisplayHeight;
            if (!ReferenceEquals(location, lastLocation)) lowerCacheValid = false;
            var scene=SceneViewport();
            cameraX=scene.X;cameraY=scene.Y;
            __state = new WorldState { Destination = target_screen ?? __instance.screen, Viewport=Game1.viewport, Active = true };
            DrawingWorld = true;
            FrameMetrics.BeginWorld();
            Game1.viewport = new xTile.Dimensions.Rectangle(cameraX, cameraY, w, expanded || UseTwoPass ? h : h * 2);
            // Extended dimensions exist ONLY during DrawWorld, never in Update or menu layout.
            AllocateLightmap(1024, expanded || UseTwoPass ? 768 : 1536);
            __instance.GraphicsDevice.SetRenderTarget(world);
            target_screen = world;
            long now = Environment.TickCount;
            if (!ReferenceEquals(location, lastLocation) || now - lastGeometryLog > 10000)
            {
                ModEntry.Write("geometry mode=" + DisplayModeController.State.Mode + "; location=" + location.Name + "; map=" + mapW + "x" + mapH + "; camera=" + cameraX + "," + cameraY + "; panel-world=" + w + "x" + h + "; ui=" + Game1.uiViewport.Width + "x" + Game1.uiViewport.Height + "; zoom=" + Game1.options.zoomLevel + "; ui-scale=" + Game1.options.uiScale);
                lastLocation = location;
                lastGeometryLog = now;
            }
        }

        private static void WorldPostfix(Game1 __instance, GameTime time, ref RenderTarget2D target_screen, WorldState __state)
        {
            if (__state == null || !__state.Active) return;
            if (UseTwoPass && (!LowerCachePrototype || ShouldRefreshLower()))
            {
                secondPass = true;
                try
                {
                    Game1.viewport = new xTile.Dimensions.Rectangle(cameraX, cameraY + WorldHeight, WorldWidth, WorldHeight);
                    __instance.GraphicsDevice.SetRenderTarget(lower);
                    __instance.DrawWorld(time, lower);
                    lowerCacheValid = true;
                    lowerCacheFrame = Environment.TickCount;
                }
                finally { secondPass = false; }
            }
            worldThisFrame = cachedWorld = true;
            RestoreWorld(__instance, __state);
            target_screen = __state.Destination;
            FrameMetrics.EndWorld();
        }
        private static Exception WorldFinalizer(Game1 __instance, WorldState __state, Exception __exception)
        {
            if (__state?.Active == true) RestoreWorld(__instance, __state);
            if (__exception != null) ModEntry.Write("world-failed " + __exception);
            return __exception;
        }
        private static void RestoreWorld(Game1 game, WorldState state)
        {
            DrawingWorld = false;
            Game1.viewport = state.Viewport;
            game.GraphicsDevice.SetRenderTarget(state.Destination);
            state.Active = false;
            UiPatches.EnsureUiViewport();
        }

        private static bool ShouldRefreshLower()
        {
            if (!lowerCacheValid) return true;
            if (Game1.player == null || Game1.currentLocation == null) return true;
            if (Game1.player.UsingTool) return true;
            int lowerTop = cameraY + WorldHeight;
            float playerY = Game1.player.Position.Y;
            const int margin = 96;
            if (playerY >= lowerTop - margin && playerY <= lowerTop + WorldHeight + margin) return true;
            return Environment.TickCount - lowerCacheFrame >= LowerCacheInterval * 16;
        }

        private static bool PresentPrefix(Game1 __instance, RenderTarget2D target_screen)
        {
            if (!Enabled || __instance.takingMapScreenshot) return true;
            FrameMetrics.BeginPresent();
            var device = __instance.GraphicsDevice;
            if (presentationSamples++ < 3)
                ModEntry.Write("presentation backbuffer=" + device.PresentationParameters.BackBufferWidth + "x" + device.PresentationParameters.BackBufferHeight + "; window=" + __instance.Window.ClientBounds + "; transport=" + __instance.localMultiplayerWindow + "; ui=" + __instance.uiScreen?.Width + "x" + __instance.uiScreen?.Height);
            device.SetRenderTarget(null);
            device.Viewport = new Viewport(0, 0, device.PresentationParameters.BackBufferWidth, device.PresentationParameters.BackBufferHeight);
            device.Clear(Color.Black);
            var batch = Game1.spriteBatch;
            bool expanded = DisplayModeController.Expanded;
            bool showWorld = worldThisFrame || (cachedWorld && Game1.gameMode == 3 && Game1.activeClickableMenu != null && ReferenceEquals(lastLocation, Game1.currentLocation));
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            if (showWorld && world != null && !world.IsDisposed)
            {
                batch.Draw(world, PhysicalUpper, new Rectangle(0, 0, WorldWidth, WorldHeight), Color.White);
                if (!expanded)
                    batch.Draw(UseTwoPass ? lower : world, PhysicalLower, new Rectangle(0, UseTwoPass ? 0 : WorldHeight, WorldWidth, WorldHeight), Color.White);
            }
            else if (__instance.screen != null)
                batch.Draw(__instance.screen, expanded ? PhysicalUpper : PhysicalLower, Color.White);
            batch.End();
            if (expanded)
            {
                var transform = Matrix.CreateScale(PhysicalLower.Width / (float)Game1.uiViewport.Width, PhysicalLower.Height / (float)Game1.uiViewport.Height, 1f)
                    * Matrix.CreateTranslation(PhysicalLower.X, PhysicalLower.Y, 0);
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
                if (Game1.activeClickableMenu == null || Game1.activeClickableMenu is DialogueBox)
                    Dashboard.Draw(batch, DisplayModeController.SelectedPage, false);
                else Dashboard.DrawBackdrop(batch);
                batch.End();
            }
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            if (Game1.activeClickableMenu is TitleMenu title)
                TitlePresentation.DrawUpper(batch, title, PhysicalUpper);
            else if (showWorld && (Game1.fadeToBlack || Game1.globalFade))
                batch.Draw(Game1.staminaRect, PhysicalUpper, Color.Black * Game1.fadeToBlackAlpha);
            if (expanded && upperUiThisFrame && upperUi != null && !upperUi.IsDisposed)
                batch.Draw(upperUi, PhysicalUpper, Color.White);
            if (__instance.uiScreen != null && !__instance.uiScreen.IsDisposed)
                batch.Draw(__instance.uiScreen, PhysicalLower, Color.White);
            batch.End();
            FrameMetrics.EndPresent();
            UiTestDriver.AfterPresent();
            return false;
        }

        private static void EnsureTargets(Game1 game)
        {
            var screen = game.screen;
            var uiScreen = game.uiScreen;
            Resize(ref screen, game.GraphicsDevice, WorldWidth, WorldHeight, "@Game1.screen");
            Resize(ref uiScreen, game.GraphicsDevice, ScreenGeometry.Width(Game1.options?.uiScale ?? 1f), ScreenGeometry.Height(Game1.options?.uiScale ?? 1f), "@Game1.uiScreen");
            if (NeedsUpperUi)
                Resize(ref upperUi, game.GraphicsDevice, uiScreen.Width, uiScreen.Height, "Reference.Dialogue");
            // The native setters dispose the previous value even for self-assignment.
            if (!ReferenceEquals(game.screen, screen)) game.screen = screen;
            if (!ReferenceEquals(game.uiScreen, uiScreen)) game.uiScreen = uiScreen;
        }
        private static void Resize(ref RenderTarget2D target, GraphicsDevice device, int width, int height, string name)
        {
            if (target != null && !target.IsDisposed && target.Width == width && target.Height == height) return;
            var old = target;
            target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents) { Name = name };
            old?.Dispose();
            cachedWorld = false;
            FrameMetrics.TargetAllocations++;
        }
        internal static void ReleaseWorld()
        {
            world?.Dispose(); lower?.Dispose(); upperUi?.Dispose();
            world = lower = upperUi = null;
            cachedWorld = worldThisFrame = false;
            lowerCacheValid = false;
            lowerCacheFrame = -1;
            lastLocation = null;
        }
        internal static void InvalidateWorld()
        {
            cachedWorld = worldThisFrame = false;
            lowerCacheValid = false;
            lowerCacheFrame = -1;
            lastGeometryLog = 0;
        }
    }
}
