using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class DialogueLayout
    {
        internal static void Install(Harmony harmony)
        {
            foreach (var signature in new[]
            {
                new[] { typeof(Dialogue) },
                new[] { typeof(string) },
                new[] { typeof(List<string>) }
            })
                harmony.Patch(AccessTools.Constructor(typeof(DialogueBox), signature),
                    transpiler: new HarmonyMethod(typeof(DialogueLayout), nameof(FitNativeWidths)),
                    postfix: new HarmonyMethod(typeof(DialogueLayout), nameof(ClampConstructed)));
            harmony.Patch(AccessTools.Constructor(typeof(DialogueBox),
                    new[] { typeof(string), typeof(Response[]), typeof(int) }),
                prefix: new HarmonyMethod(typeof(DialogueLayout), nameof(QuestionWidth)),
                postfix: new HarmonyMethod(typeof(DialogueLayout), nameof(ClampConstructed)));
            foreach (string method in new[] { nameof(DialogueBox.receiveLeftClick), nameof(DialogueBox.gameWindowSizeChanged) })
                harmony.Patch(AccessTools.Method(typeof(DialogueBox), method),
                    transpiler: new HarmonyMethod(typeof(DialogueLayout), nameof(FitNativeWidths)),
                    postfix: method == nameof(DialogueBox.gameWindowSizeChanged)
                        ? new HarmonyMethod(typeof(DialogueLayout), nameof(ClampConstructed)) : null);
        }

        internal static int FitWidth(int original)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return original;
            UiPatches.EnsureUiViewport();
            // Native drawBox corners extend 44px left and 40px right of its body.
            return Math.Min(original, Math.Max(1, Game1.uiViewport.Width - 96));
        }

        [HarmonyAfter("Mangupix.DialogueDisplayFrameworkContinued", "aedenthorn.DialogueDisplayFramework")]
        [HarmonyPriority(Priority.Last)]
        private static void ClampConstructed(DialogueBox __instance)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return;
            UiPatches.EnsureUiViewport();
            int maxWidth = Math.Max(1, Game1.uiViewport.Width - 96);
            int maxHeight = Math.Max(1, Game1.uiViewport.Height - 96);
            __instance.width = Math.Min(__instance.width, maxWidth);
            __instance.height = Math.Min(__instance.height, maxHeight);
            if (__instance.heightForQuestions > 0)
                __instance.heightForQuestions = Math.Min(__instance.heightForQuestions, maxHeight);
            __instance.x = Math.Clamp((Game1.uiViewport.Width - __instance.width) / 2, 0, Math.Max(0, Game1.uiViewport.Width - __instance.width));
            __instance.y = Math.Clamp(Game1.uiViewport.Height - __instance.height - 64, 0, Math.Max(0, Game1.uiViewport.Height - __instance.height));
            __instance.xPositionOnScreen = __instance.x;
            __instance.yPositionOnScreen = __instance.y;
            __instance.friendshipJewel = new Microsoft.Xna.Framework.Rectangle(__instance.x + __instance.width - 64, __instance.y + 256, 44, 44);
        }

        private static void QuestionWidth(ref int width) => width = FitWidth(width);

        private static IEnumerable<CodeInstruction> FitNativeWidths(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                yield return code;
                if (code.LoadsConstant(1200) || code.LoadsConstant(1240))
                    yield return CodeInstruction.Call(typeof(DialogueLayout), nameof(FitWidth));
            }
        }
    }
}
