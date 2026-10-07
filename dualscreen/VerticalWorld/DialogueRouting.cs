using System;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class DialogueRouting
    {
        private static bool routingMenu;
        internal sealed class RouteState
        {
            internal RenderTargetBinding[] Targets;
            internal Viewport Viewport;
            internal bool SplitBatch;
        }
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Game1), "DrawMenu"), prefix: new HarmonyMethod(typeof(DialogueRouting), nameof(MenuPrefix)), finalizer: new HarmonyMethod(typeof(DialogueRouting), nameof(RouteFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "SetRenderTarget"), prefix: new HarmonyMethod(typeof(DialogueRouting), nameof(TargetPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "drawDialogueBox", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(DialogueRouting), nameof(LegacyPrefix)), finalizer: new HarmonyMethod(typeof(DialogueRouting), nameof(RouteFinalizer)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Game1), "IsHudDrawn"), prefix: new HarmonyMethod(typeof(DialogueRouting), nameof(HudPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Game1), "drawMouseCursor"), prefix: new HarmonyMethod(typeof(DialogueRouting), nameof(CursorPrefix)));
        }
        private static bool HudPrefix(ref bool __result)
        {
            if (!DisplayModeController.Expanded) return true;
            __result = false; // Also disables input on the hidden vanilla HUD controls.
            return false;
        }
        private static bool CursorPrefix() => !DisplayModeController.Expanded || Game1.activeClickableMenu != null && !(Game1.activeClickableMenu is DialogueBox);
        private static RouteState Capture(bool splitBatch)
        {
            var device = Game1.graphics.GraphicsDevice;
            return new RouteState { Targets = device.GetRenderTargets(), Viewport = device.Viewport, SplitBatch = splitBatch };
        }
        private static void MenuPrefix(out RouteState __state)
        {
            __state = null;
            if (!DisplayModeController.Expanded || !(Game1.activeClickableMenu is DialogueBox)) return;
            __state = Capture(false);
            routingMenu = true;
        }
        private static void TargetPrefix(ref RenderTarget2D target)
        {
            if (routingMenu && ReferenceEquals(target, Game1.game1.uiScreen)) target = VerticalWorldRenderer.UpperUi;
        }
        private static void LegacyPrefix(out RouteState __state)
        {
            __state = null;
            if (!DisplayModeController.Expanded) return;
            __state = Capture(true);
            // The legacy dialogue is called inside an existing deferred SpriteBatch.
            Game1.spriteBatch.End();
            Game1.graphics.GraphicsDevice.SetRenderTarget(VerticalWorldRenderer.UpperUi);
            BeginBatch();
        }
        private static void BeginBatch() => Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        private static Exception RouteFinalizer(RouteState __state, Exception __exception)
        {
            if (__state == null) return __exception;
            try
            {
                if (__state.SplitBatch) Game1.spriteBatch.End();
                var device = Game1.graphics.GraphicsDevice;
                device.SetRenderTargets(__state.Targets);
                device.Viewport = __state.Viewport;
                if (__state.SplitBatch) BeginBatch();
            }
            finally { routingMenu = false; }
            return __exception;
        }
    }
}
