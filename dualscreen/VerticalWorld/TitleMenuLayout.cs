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
    internal static class TitleMenuLayout
    {
        internal const float Scale = 1.5f * 1024f / 1366f;
        internal static bool Installed { get; private set; }
        internal static bool UsesLargeScale => Installed && VerticalWorldRenderer.Enabled
            && Game1.activeClickableMenu is TitleMenu
            && (TitleMenu.subMenu is AdvancedGameOptions || TitleMenu.subMenu is LoadGameMenu
                || TitleMenu.subMenu is TitleTextInputMenu);

        internal static void Install(Harmony parent)
        {
            var harmony = new Harmony(parent.Id + ".TitleMenus");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(AdvancedGameOptions), "ResetComponents"),
                    prefix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(AdvancedBounds)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(AdvancedFooter)));
                harmony.Patch(AccessTools.Method(typeof(LoadGameMenu), nameof(LoadGameMenu.UpdateButtons)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(LoadBounds)));
                harmony.Patch(AccessTools.Method(typeof(LoadGameMenu), nameof(LoadGameMenu.gameWindowSizeChanged)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(LoadBounds)));
                harmony.Patch(AccessTools.Method(typeof(LoadGameMenu), nameof(LoadGameMenu.update)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(LoadBounds)));
                harmony.Patch(AccessTools.Method(typeof(CoopMenu), nameof(CoopMenu.gameWindowSizeChanged)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(CoopBounds)));
                harmony.Patch(AccessTools.Method(typeof(CoopMenu), "connectionFinished"),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(CoopBounds)));
                harmony.Patch(AccessTools.Method(typeof(CoopMenu), nameof(CoopMenu.SetTab)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(CoopBounds)));
                foreach (string method in new[] { "customSnapBehavior", "leftClickHeld", "setScrollBarToCurrentIndex",
                    "receiveScrollWheelAction", "receiveLeftClick", "update", "draw" })
                    harmony.Patch(AccessTools.Method(typeof(LoadGameMenu), method,
                            method == "draw" ? new[] { typeof(SpriteBatch) } : null),
                        transpiler: new HarmonyMethod(typeof(TitleMenuLayout), nameof(PageCounts)));
                harmony.Patch(AccessTools.Method(typeof(LoadGameMenu.SaveFileSlot), nameof(LoadGameMenu.SaveFileSlot.Draw)),
                    prefix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(SaveRow)));
                harmony.Patch(AccessTools.Constructor(typeof(TitleTextInputMenu), new[] { typeof(string),
                        typeof(NamingMenu.doneNamingBehavior), typeof(string), typeof(string), typeof(bool) }),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(TextInputBounds)));
                harmony.Patch(AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.gameWindowSizeChanged)),
                    postfix: new HarmonyMethod(typeof(TitleMenuLayout), nameof(ResizeTextInput)));
                Installed = true;
            }
            catch (Exception ex)
            {
                Installed = false;
                harmony.UnpatchAll(harmony.Id);
                ModEntry.Write("title-menu-layout-install-failed " + ex);
            }
        }

        private static bool TitleContext => VerticalWorldRenderer.Enabled && Game1.activeClickableMenu is TitleMenu;
        private static int PanelWidth => ScreenGeometry.Width(Scale);
        private static int PanelHeight => ScreenGeometry.Height(Scale);
        private static int PageCount(LoadGameMenu menu) => TitleContext ? 2 : 4;

        private static void ResizeTextInput()
        {
            if (TitleMenu.subMenu is TitleTextInputMenu input) TextInputBounds(input);
        }

        private static void TextInputBounds(TitleTextInputMenu __instance)
        {
            if (!TitleContext) return;
            var menu = __instance;
            menu.width = PanelWidth;
            menu.height = PanelHeight;
            menu.textBox.X = (PanelWidth - 720) / 2;
            menu.textBox.Y = PanelHeight / 2;
            menu.textBox.Width = 512;
            // The native dialogue-box renderer requires 192px including its frame insets.
            menu.textBox.Height = 192;
            menu.textBoxCC.bounds = new Rectangle(menu.textBox.X, menu.textBox.Y, 512, 64);
            menu.doneNamingButton.bounds.Location = new Point(menu.textBox.X + 548, menu.textBox.Y - 8);
            menu.pasteButton.bounds.Location = new Point(menu.doneNamingButton.bounds.Right + 16, menu.textBox.Y - 8);
        }

        // Replace page limits, while leaving 4x sprite scales and frame padding intact.
        private static IEnumerable<CodeInstruction> PageCounts(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (i > 0 && codes[i].LoadsConstant(4) && codes[i - 1].operand is MethodInfo count
                    && count.Name == "get_Count")
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(codes[i].labels);
                    load.blocks.AddRange(codes[i].blocks);
                    yield return load;
                    yield return CodeInstruction.Call(typeof(TitleMenuLayout), nameof(PageCount));
                }
                else if (codes[i].LoadsConstant(3) && i + 1 < codes.Count
                    && codes[i + 1].operand is MethodInfo component && component.Name == "getComponentWithID")
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(codes[i].labels);
                    load.blocks.AddRange(codes[i].blocks);
                    yield return load;
                    yield return CodeInstruction.Call(typeof(TitleMenuLayout), nameof(PageCount));
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Sub);
                }
                else yield return codes[i];
            }
        }

        private static void AdvancedBounds(AdvancedGameOptions __instance)
        {
            if (!TitleContext) return;
            __instance.xPositionOnScreen = (PanelWidth - 868) / 2;
            __instance.yPositionOnScreen = (PanelHeight - 620) / 2;
            __instance.width = 800;
            __instance.height = 500;
        }

        private static void AdvancedFooter(AdvancedGameOptions __instance)
        {
            if (!TitleContext) return;
            __instance.okButton.bounds.Location = new Point(__instance.xPositionOnScreen,
                __instance.yPositionOnScreen + 540);
        }

        private static void LoadBounds(LoadGameMenu __instance)
        {
            if (!TitleContext) return;
            var menu = __instance;
            menu.width = 820;
            menu.height = 464;
            menu.xPositionOnScreen = (PanelWidth - 900) / 2;
            menu.yPositionOnScreen = menu is CoopMenu ? 96 : 48;
            if (menu.slotButtons.Count > 2) menu.slotButtons.RemoveRange(2, menu.slotButtons.Count - 2);
            if (menu.deleteButtons.Count > 2) menu.deleteButtons.RemoveRange(2, menu.deleteButtons.Count - 2);
            for (int i = 0; i < menu.slotButtons.Count; i++)
            {
                menu.slotButtons[i].bounds = new Rectangle(menu.xPositionOnScreen + 16,
                    menu.yPositionOnScreen + 16 + i * 232, menu.width - 32, 232);
                menu.slotButtons[i].downNeighborID = i == 1 ? -7777 : -99998;
            }
            for (int i = 0; i < menu.deleteButtons.Count; i++)
                menu.deleteButtons[i].bounds.Location = new Point(menu.xPositionOnScreen + menu.width - 68,
                    menu.yPositionOnScreen + 36 + i * 232);
            menu.upArrow.bounds.Location = new Point(menu.xPositionOnScreen + menu.width + 16, menu.yPositionOnScreen + 16);
            menu.downArrow.bounds.Location = new Point(menu.upArrow.bounds.X, menu.yPositionOnScreen + menu.height - 64);
            var runner = new Rectangle(menu.upArrow.bounds.X + 12, menu.upArrow.bounds.Bottom + 4, 24,
                menu.height - 64 - menu.upArrow.bounds.Height - 28);
            AccessTools.Field(typeof(LoadGameMenu), "scrollBarRunner").SetValue(menu, runner);
            int maximum = Math.Max(0, menu.MenuSlots.Count - 2);
            menu.currentItemIndex = Math.Clamp(menu.currentItemIndex, 0, maximum);
            menu.scrollBar.bounds.Location = new Point(runner.X, menu.currentItemIndex == maximum && maximum > 0
                ? menu.downArrow.bounds.Y - menu.scrollBar.bounds.Height - 4
                : runner.Y + runner.Height / Math.Max(1, maximum + 1) * menu.currentItemIndex);
            menu.backButton.bounds = new Rectangle(PanelWidth - 222, PanelHeight - 89, 198, 81);
            if (Game1.activeClickableMenu is TitleMenu title && ReferenceEquals(TitleMenu.subMenu, menu))
                title.backButton.bounds = menu.backButton.bounds;
            if (menu is CoopMenu coop) CoopBounds(coop);
        }

        private static void CoopBounds(CoopMenu __instance)
        {
            if (!TitleContext || __instance.joinTab == null || __instance.hostTab == null) return;
            var menu = __instance;
            AccessTools.Field(typeof(CoopMenu), "smallScreenFormat").SetValue(menu, true);
            menu.joinTab.bounds.Location = new Point(menu.xPositionOnScreen, 16);
            menu.hostTab.bounds.Location = new Point(menu.joinTab.bounds.Right, 16);
            if (menu.refreshButton != null)
            {
                menu.refreshButton.bounds.Location = new Point(menu.xPositionOnScreen + 16, PanelHeight - 96);
                menu.refreshButton.bounds.Height = 80;
            }
        }

        private static readonly Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int> FarmerPortrait =
            (Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int>)Delegate.CreateDelegate(
                typeof(Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int>),
                AccessTools.Method(typeof(LoadGameMenu.SaveFileSlot), "drawSlotFarmer"));
        private static readonly Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int> FarmerShadow =
            (Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int>)Delegate.CreateDelegate(
                typeof(Action<LoadGameMenu.SaveFileSlot, SpriteBatch, int>),
                AccessTools.Method(typeof(LoadGameMenu.SaveFileSlot), "drawSlotShadow"));

        private static bool SaveRow(LoadGameMenu.SaveFileSlot __instance, SpriteBatch b, int i)
        {
            if (!TitleContext) return true;
            var menu = (LoadGameMenu)AccessTools.Field(typeof(LoadGameMenu.MenuSlot), "menu").GetValue(__instance);
            var bounds = menu.slotButtons[i].bounds;
            var farmer = __instance.Farmer;
            float alpha = __instance.getSlotAlpha();
            void Text(string value, int x, int y, int width)
            {
                string text = value ?? "";
                while (text.Length > 1 && Game1.smallFont.MeasureString(text).X > width)
                    text = text.Substring(0, text.Length - 2) + "…";
                Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(bounds.X + x, bounds.Y + y),
                    Game1.textColor * alpha);
            }
            Text((__instance.SlotNumber ?? menu.currentItemIndex + i + 1) + ".", 16, 28, 64);
            Text(farmer.Name, 172, 20, bounds.Width - 264);
            Text(Game1.content.LoadString("Strings\\StringsFromCSFiles:LoadGameMenu.cs.11019", farmer.farmName),
                172, 64, bounds.Width - 196);
            if (__instance.versionComparison < 0)
            {
                Text(Game1.content.LoadString("Strings\\UI:VersionMismatch", farmer.gameVersion),
                    172, 108, bounds.Width - 196);
                return false;
            }
            FarmerShadow(__instance, b, i);
            FarmerPortrait(__instance, b, i);
            string date = farmer.dayOfMonthForSaveGame.HasValue && farmer.seasonForSaveGame.HasValue && farmer.yearForSaveGame.HasValue
                ? Utility.getDateStringFor(farmer.dayOfMonthForSaveGame.Value, farmer.seasonForSaveGame.Value, farmer.yearForSaveGame.Value)
                : farmer.dateStringForSaveGame;
            Text(date, 172, 108, bounds.Width - 196);
            Text(Game1.content.LoadString("Strings\\StringsFromCSFiles:LoadGameMenu.cs.11020", Utility.getNumberWithCommas(farmer.Money)),
                172, 156, 272);
            Text(Utility.getHoursMinutesStringFromMilliseconds(farmer.millisecondsPlayed), 488, 156, bounds.Width - 512);
            return false;
        }
    }
}
