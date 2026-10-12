using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class LetterLayout
    {
        private sealed class TextState { internal string Text; }
        private static readonly ConditionalWeakTable<LetterViewerMenu, TextState> texts = new();
        internal static int FitWidth(int original) => VerticalWorldRenderer.Enabled ? Math.Min(original, Game1.uiViewport.Width - 64) : original;
        internal static int FitHeight(int original) => VerticalWorldRenderer.Enabled ? Math.Min(original, Game1.uiViewport.Height - 64) : original;
        internal static void Install(Harmony harmony)
        {
            foreach (var ctor in typeof(LetterViewerMenu).GetConstructors())
                harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(LetterLayout), nameof(Dimensions)),
                    postfix: new HarmonyMethod(typeof(LetterLayout), nameof(Created)));
            harmony.Patch(AccessTools.Method(typeof(LetterViewerMenu), nameof(LetterViewerMenu.gameWindowSizeChanged)),
                prefix: new HarmonyMethod(typeof(LetterLayout), nameof(Resize)));
            harmony.Patch(AccessTools.Method(typeof(LetterViewerMenu), nameof(LetterViewerMenu.draw), new[] { typeof(SpriteBatch) }),
                transpiler: new HarmonyMethod(typeof(LetterLayout), nameof(Background)));
        }
        private static IEnumerable<CodeInstruction> Dimensions(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                yield return code;
                if (code.LoadsConstant(1280)) yield return CodeInstruction.Call(typeof(LetterLayout), nameof(FitWidth));
                if (code.LoadsConstant(720)) yield return CodeInstruction.Call(typeof(LetterLayout), nameof(FitHeight));
            }
        }
        private static void Created(LetterViewerMenu __instance)
        {
            if (!VerticalWorldRenderer.Enabled) return;
            texts.GetOrCreateValue(__instance).Text = string.Join("", __instance.mailMessage);
            PositionControls(__instance);
        }
        private static bool Resize(LetterViewerMenu __instance)
        {
            if (!VerticalWorldRenderer.Enabled) return true;
            var menu = __instance;
            menu.width = FitWidth(1280); menu.height = FitHeight(720);
            menu.xPositionOnScreen = (Game1.uiViewport.Width - menu.width) / 2;
            menu.yPositionOnScreen = (Game1.uiViewport.Height - menu.height) / 2;
            if (menu.secretNoteImage < 0 && texts.TryGetValue(menu, out var state))
            {
                menu.mailMessage = SpriteText.getStringBrokenIntoSectionsOfHeight(state.Text, menu.width - 64, menu.height - 128);
                menu.page = Math.Clamp(menu.page, 0, Math.Max(0, menu.mailMessage.Count - 1));
            }
            PositionControls(menu);
            menu.OnPageChange();
            return false;
        }
        private static void PositionControls(LetterViewerMenu menu)
        {
            int x=menu.xPositionOnScreen, y=menu.yPositionOnScreen;
            if (menu.upperRightCloseButton != null) menu.upperRightCloseButton.bounds = new Rectangle(x+menu.width-48,y-24,48,48);
            if (menu.backButton != null) menu.backButton.bounds = new Rectangle(x+32,y+menu.height-96,48,44);
            if (menu.forwardButton != null) menu.forwardButton.bounds = new Rectangle(x+menu.width-80,y+menu.height-96,48,44);
            foreach (var item in menu.itemsToGrab) item.bounds = new Rectangle(x+menu.width/2-48,y+menu.height-128,96,96);
            if (menu.acceptQuestButton != null)
            {
                var bounds=menu.acceptQuestButton.bounds;
                bounds.X=x+(menu.width-bounds.Width)/2; bounds.Y=y+menu.height-128;
                menu.acceptQuestButton.bounds=bounds;
            }
            menu.populateClickableComponentList();
        }
        private static IEnumerable<CodeInstruction> Background(IEnumerable<CodeInstruction> instructions)
        {
            var draw = AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] {
                typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float),
                typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) });
            bool replaced=false;
            foreach (var code in instructions)
            {
                if (!replaced && code.Calls(draw))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(code.labels); load.blocks.AddRange(code.blocks);
                    yield return load;
                    yield return CodeInstruction.Call(typeof(LetterLayout), nameof(DrawBackground));
                    replaced=true;
                }
                else yield return code;
            }
            if (!replaced) throw new InvalidOperationException("Letter background draw not found");
        }
        private static void DrawBackground(SpriteBatch batch, Texture2D texture, Vector2 position, Rectangle? source,
            Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth, LetterViewerMenu menu)
        {
            var size = VerticalWorldRenderer.Enabled ? new Vector2(menu.width/320f, menu.height/180f)*(scale/4f) : new Vector2(scale);
            batch.Draw(texture,position,source,color,rotation,origin,size,effects,depth);
        }
    }
}
