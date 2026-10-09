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
                    transpiler: new HarmonyMethod(typeof(DialogueLayout), nameof(FitNativeWidths)));
            harmony.Patch(AccessTools.Constructor(typeof(DialogueBox),
                    new[] { typeof(string), typeof(Response[]), typeof(int) }),
                prefix: new HarmonyMethod(typeof(DialogueLayout), nameof(QuestionWidth)));
            foreach (string method in new[] { nameof(DialogueBox.receiveLeftClick), nameof(DialogueBox.gameWindowSizeChanged) })
                harmony.Patch(AccessTools.Method(typeof(DialogueBox), method),
                    transpiler: new HarmonyMethod(typeof(DialogueLayout), nameof(FitNativeWidths)));
        }

        internal static int FitWidth(int original)
        {
            if (!VerticalWorldRenderer.Enabled || Game1.game1?.takingMapScreenshot == true) return original;
            UiPatches.EnsureUiViewport();
            // Native drawBox corners extend 44px left and 40px right of its body.
            return Math.Min(original, Math.Max(1, Game1.uiViewport.Width - 96));
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
