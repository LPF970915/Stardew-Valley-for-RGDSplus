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
    internal static class UiPatches
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.CanShowPauseMenu)),
                prefix: new HarmonyMethod(typeof(UiPatches), nameof(PauseMenuPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "SetWindowSize"),
                prefix: new HarmonyMethod(typeof(UiPatches), nameof(ResizePrefix)),
                postfix: new HarmonyMethod(typeof(UiPatches), nameof(ResizePostfix)),
                transpiler: new HarmonyMethod(typeof(UiPatches), nameof(ResizeTranspiler)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "PushUIMode"), postfix: new HarmonyMethod(typeof(UiPatches), nameof(EnsureUiViewport)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "updateViewportForScreenSizeChange"), prefix: new HarmonyMethod(typeof(UiPatches), nameof(ViewportSizePrefix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Options), "zoomLevel"), prefix: new HarmonyMethod(typeof(UiPatches), nameof(ZoomPrefix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Options), "uiScale"), prefix: new HarmonyMethod(typeof(UiPatches), nameof(UiScalePrefix)));
            harmony.Patch(AccessTools.PropertySetter(typeof(Options), "desiredUIScale"), postfix: new HarmonyMethod(typeof(UiPatches), nameof(UiScaleChanged)));
            harmony.Patch(AccessTools.Method(typeof(Toolbar), "draw", new[] { typeof(SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(UiPatches), nameof(ToolbarPrefix)),
                finalizer: new HarmonyMethod(typeof(UiPatches), nameof(ToolbarFinalizer)));
        }
        private static void UiScaleChanged(float value) => CompanionPreferences.RememberUiScale(value);

        private static bool PauseMenuPrefix(ref bool __result)
        {
            if (!DisplayModeController.Expanded) return true;
            __result = false;
            return false;
        }

        // Preserve physical ApplyChanges; narrow dimensions AFTER it, before native
        // allocation, menu construction and resize notifications.
        internal static IEnumerable<CodeInstruction> ResizeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            bool afterApply = false;
            int applyCount = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (code.operand is MethodInfo method)
                {
                    if (method.Name == "ApplyChanges" && method.DeclaringType == typeof(GraphicsDeviceManager))
                    {
                        applyCount++;
                        afterApply = true;
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return CodeInstruction.Call(typeof(UiPatches), nameof(LogicalWidth));
                        yield return new CodeInstruction(OpCodes.Starg_S, (byte)1);
                        yield return new CodeInstruction(OpCodes.Ldarg_2);
                        yield return CodeInstruction.Call(typeof(UiPatches), nameof(LogicalHeight));
                        yield return new CodeInstruction(OpCodes.Starg_S, (byte)2);
                    }
                    else if (afterApply && method.Name == "get_PreferredBackBufferWidth")
                        yield return CodeInstruction.Call(typeof(UiPatches), nameof(LogicalWidth));
                    else if (afterApply && method.Name == "get_PreferredBackBufferHeight")
                        yield return CodeInstruction.Call(typeof(UiPatches), nameof(LogicalHeight));
                }
            }
            if (applyCount != 1) throw new InvalidOperationException("SetWindowSize physical/logical boundary not found exactly once.");
        }
        private static int LogicalWidth(int original) => VerticalWorldRenderer.Enabled ? 1024 : original;
        private static int LogicalHeight(int original) => VerticalWorldRenderer.Enabled ? 768 : original;
        private static void ViewportSizePrefix(ref int width, ref int height)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1.takingMapScreenshot) return;
            width = 1024; height = 768;
        }
        private static bool ZoomPrefix(Options __instance, ref float __result)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return true;
            __result = ScreenGeometry.ValidScale(__instance.baseZoomLevel);
            return false;
        }
        private static bool UiScalePrefix(Options __instance, ref float __result)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return true;
            // Native PortMaster single-panel fit, independently multiplied by the UI setting.
            __result = ScreenGeometry.ValidScale(__instance.baseUIScale) * (1024f / 1366f);
            if (DisplayModeController.Expanded) __result = Math.Min(__result, DisplayModeController.MenuScaleLimit);
            return false;
        }
        internal static void EnsureViewports()
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return;
            if (!VerticalWorldRenderer.DrawingWorld)
            {
                Game1.viewport.Width = VerticalWorldRenderer.WorldWidth;
                Game1.viewport.Height = VerticalWorldRenderer.WorldHeight;
            }
            EnsureUiViewport();
        }
        internal static void EnsureUiViewport()
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return;
            float scale = Game1.options?.uiScale ?? 1f;
            Game1.uiViewport = new xTile.Dimensions.Rectangle(Game1.viewport.X, Game1.viewport.Y, ScreenGeometry.Width(scale), ScreenGeometry.Height(scale));
        }
        private static void ResizePrefix(Game1 __instance, out RenderTarget2D[] __state)
        {
            __state = VerticalWorldRenderer.Enabled ? new[] { __instance.screen, __instance.uiScreen } : null;
        }
        private static void ResizePostfix(Game1 __instance, RenderTarget2D[] __state)
        {
            if (__state == null) return;
            foreach (var old in __state)
                if (old != null && old != __instance.screen && old != __instance.uiScreen && !old.IsDisposed) old.Dispose();
            // GameRunner uses these for physical submission, outside menu layout.
            var pp = __instance.GraphicsDevice.PresentationParameters;
            __instance.localMultiplayerWindow = new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
            Game1.defaultDeviceViewport = new Viewport(__instance.localMultiplayerWindow);
            EnsureViewports();
        }
        private static void ToolbarPrefix(Toolbar __instance, out bool? __state)
        {
            __state = null;
            if (!VerticalWorldRenderer.Enabled) return;
            __state = Game1.options.pinToolbarToggle;
            Game1.options.pinToolbarToggle = true;
            __instance.yPositionOnScreen = Game1.uiViewport.Height + 8 - Utility.makeSafeMarginY(8);
            for (int i = 0; i < __instance.buttons.Count; i++)
                __instance.buttons[i].bounds = new Rectangle(Game1.uiViewport.Width / 2 - 384 + i * 64, __instance.yPositionOnScreen - 88, 64, 64);
        }
        private static Exception ToolbarFinalizer(bool? __state, Exception __exception)
        {
            if (__state.HasValue) Game1.options.pinToolbarToggle = __state.Value;
            return __exception;
        }
    }
}
