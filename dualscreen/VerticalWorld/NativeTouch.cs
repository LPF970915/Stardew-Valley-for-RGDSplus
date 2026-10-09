using System;
using System.Diagnostics;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    // Native text boxes read Game1's cursor instead of receiveLeftClick's x/y.
    // Keep dispatch in UI space and route the keyboard before its parent menu.
    internal static class NativeTouch
    {
        internal static bool Dispatching { get; private set; }
        internal static IClickableMenu Target => Game1.textEntry ?? Game1.activeClickableMenu;
        private static Point point;
        private static TextBox pendingTextBox;
        private static long started, pendingFrame;
        private static string targetName;
        private static int presentationRequests;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TextBox), nameof(TextBox.Update)),
                prefix: new HarmonyMethod(typeof(NativeTouch), nameof(UpdateTextBox)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Options), nameof(Options.SnappyMenus)),
                prefix: new HarmonyMethod(typeof(NativeTouch), nameof(PointerMenus)));
        }

        private static bool PointerMenus(ref bool __result)
        {
            if (!Dispatching) return true;
            __result = false;
            return false;
        }

        private static bool UpdateTextBox(TextBox __instance)
        {
            if (!Dispatching) return true;
            __instance.Selected = new Rectangle(__instance.X, __instance.Y, __instance.Width, __instance.Height).Contains(point);
            if (__instance.Selected) pendingTextBox = __instance;
            return false;
        }

        internal static void Begin(Point position)
        {
            started = Stopwatch.GetTimestamp();
            targetName = Game1.textEntry != null ? "TextEntryMenu"
                : (Game1.activeClickableMenu is TitleMenu ? TitleMenu.subMenu?.GetType().Name : null)
                    ?? Game1.activeClickableMenu?.GetType().Name ?? "none";
            point = position;
            pendingTextBox = null;
            Dispatching = true;
            Game1.PushUIMode();
            MoveCursor(position);
        }

        internal static void End()
        {
            try
            {
                if (pendingTextBox != null && Game1.textEntry == null)
                    Game1.showTextEntry(pendingTextBox);
            }
            finally
            {
                Dispatching = false;
                pendingTextBox = null;
                Game1.PopUIMode();
                pendingFrame = started;
                ModEntry.Write("touch-action target=" + targetName + "; point=" + point
                    + "; elapsed-ms=" + Elapsed(started).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        internal static void MoveCursor(Point position)
        {
            Game1.setMousePosition(position, true);
            Game1.timerUntilMouseFade = 4000;
        }

        internal static void Continue(Point position, bool release)
        {
            Game1.PushUIMode();
            bool previousDispatching = Dispatching;
            Dispatching = true;
            try
            {
                MoveCursor(position);
                var target = Target;
                if (release) target?.releaseLeftClick(position.X, position.Y);
                else target?.leftClickHeld(position.X, position.Y);
            }
            finally
            {
                Dispatching = previousDispatching;
                Game1.PopUIMode();
            }
        }

        internal static void AfterFrame()
        {
            if (pendingFrame == 0) return;
            ModEntry.Write("touch-present target=" + targetName + "; elapsed-ms="
                + Elapsed(pendingFrame).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            pendingFrame = 0;
        }

        private static double Elapsed(long stamp) => (Stopwatch.GetTimestamp() - stamp) * 1000.0 / Stopwatch.Frequency;

        internal static void PresentWindow()
        {
            if (!VerticalWorldRenderer.Enabled || presentationRequests >= 8) return;
            // Production launches need the same firmware placement/activation as QA.
            // Do this only during startup, never repeatedly steal focus during play.
            presentationRequests++;
            var window = Game1.game1.Window;
            window.Title = "flags=stay-on-top";
            window.Title = "attrs=pos:0,0";
            window.Title = "requests=activate";
            window.Title = "Stardew Valley";
        }
    }
}
