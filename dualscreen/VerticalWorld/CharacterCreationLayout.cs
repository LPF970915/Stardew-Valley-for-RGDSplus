using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    // Keep the game's widgets and hitboxes; remove desktop padding for the lower panel.
    internal static class CharacterCreationLayout
    {
        internal static bool Installed { get; private set; }
        internal static bool Active => VerticalWorldRenderer.Enabled
            && Installed
            && Game1.activeClickableMenu is TitleMenu
            && TitleMenu.subMenu is CharacterCustomization menu && Applies(menu);

        private static bool Applies(CharacterCustomization menu) => VerticalWorldRenderer.Enabled
            && menu.source == CharacterCustomization.Source.NewGame;

        internal static void Install(Harmony harmony)
        {
            // Optional menu patches must not remove the dual-screen renderer on failure.
            var menuHarmony = new Harmony(harmony.Id + ".CharacterCreation");
            try
            {
                InstallMenu(menuHarmony);
                Installed = true;
            }
            catch (Exception ex)
            {
                Installed = false;
                menuHarmony.UnpatchAll(menuHarmony.Id);
                ModEntry.Write("character-layout-install-failed " + ex);
            }
        }

        private static void InstallMenu(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertySetter(typeof(TitleMenu), nameof(TitleMenu.subMenu)),
                prefix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(BeforeSubMenu)),
                postfix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(AfterSubMenu)));
            harmony.Patch(AccessTools.Method(typeof(CharacterCustomization), "ResetComponents"),
                prefix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(BeforeReset)),
                postfix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(Layout)));
            harmony.Patch(AccessTools.Method(typeof(TitleMenu), nameof(TitleMenu.gameWindowSizeChanged)),
                postfix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(PositionBack)));
            harmony.Patch(AccessTools.Method(typeof(CharacterCustomization), nameof(CharacterCustomization.RefreshFarmTypeButtons)),
                postfix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(LayoutFarms)));
            harmony.Patch(AccessTools.Method(typeof(CharacterCustomization), nameof(CharacterCustomization.draw), new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) }),
                prefix: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(CharacterCreationLayout), nameof(AfterDraw)));
        }

        private static void BeforeDraw(CharacterCustomization __instance, out bool __state)
        {
            __state = Applies(__instance) && !__instance.showingCoopHelp;
            if (!__state) return;
            // drawDialogueBox adds a 64px top inset even for drawOnlyBox.
            __instance.yPositionOnScreen -= 64;
            __instance.height += 64;
        }

        private static void AfterDraw(CharacterCustomization __instance, bool __state)
        {
            if (!__state) return;
            __instance.yPositionOnScreen += 64;
            __instance.height -= 64;
        }

        private static void BeforeSubMenu(out bool __state) => __state = Active || TitleMenuLayout.UsesLargeScale;

        private static void BeforeReset(CharacterCustomization __instance)
        {
            if (!Applies(__instance)) return;
            __instance.width = 632 + IClickableMenu.borderWidth * 2;
            __instance.height = 648 + IClickableMenu.borderWidth * 2 + 64;
        }

        private static void PositionBack(TitleMenu __instance)
        {
            if (Active && TitleMenu.subMenu is CharacterCustomization menu)
                __instance.backButton.bounds = menu.backButton.bounds;
            else if (TitleMenuLayout.UsesLargeScale && TitleMenu.subMenu is LoadGameMenu load)
                __instance.backButton.bounds = load.backButton.bounds;
        }

        private static void AfterSubMenu(bool __state)
        {
            if ((!__state && !Active && !TitleMenuLayout.UsesLargeScale) || Game1.activeClickableMenu is not TitleMenu title) return;
            // TitleMenu.Dispose clears menuContent before it clears subMenu. The
            // setter still runs during that teardown; don't rebuild icons from a
            // disposed content manager while a farmhand is entering the world.
            if (title.menuContent == null) return;
            var old = new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
            UiPatches.EnsureUiViewport();
            title.gameWindowSizeChanged(old, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height));
        }

        private static void Layout(CharacterCustomization __instance)
        {
            var menu = __instance;
            if (!Applies(menu)) return;
            int x = (Game1.uiViewport.Width - 900) / 2;
            int y = (Game1.uiViewport.Height - 660) / 2;
            int dx = x - menu.xPositionOnScreen - 24;
            int dy = y - menu.yPositionOnScreen - 96;
            var moved = new HashSet<ClickableComponent>();
            void Move(ClickableComponent c)
            {
                if (c != null && moved.Add(c)) c.bounds.Offset(dx, dy);
            }
            foreach (var c in menu.labels) Move(c);
            foreach (var c in menu.leftSelectionButtons) Move(c);
            foreach (var c in menu.rightSelectionButtons) Move(c);
            foreach (var c in menu.genderButtons) Move(c);
            foreach (var c in menu.colorPickerCCs) Move(c);
            Move(menu.nameBoxCC); Move(menu.farmnameBoxCC); Move(menu.favThingBoxCC);
            Move(menu.randomButton); Move(menu.skipIntroButton); Move(menu.okButton);
            // Keep the last appearance value above the lower frame border.
            foreach (var c in menu.leftSelectionButtons)
                if (c.name == "Acc") c.bounds.Offset(0, -12);
            foreach (var c in menu.rightSelectionButtons)
                if (c.name == "Acc") c.bounds.Offset(0, -12);
            var accessoryLabel = (ClickableComponent)AccessTools.Field(typeof(CharacterCustomization), "accLabel").GetValue(menu);
            accessoryLabel?.bounds.Offset(0, -12);
            foreach (string name in new[] { "nameBox", "farmnameBox", "favThingBox" })
            {
                var box = (TextBox)AccessTools.Field(typeof(CharacterCustomization), name).GetValue(menu);
                box.X += dx; box.Y += dy;
            }
            menu.portraitBox.Offset(dx, dy);
            if (menu.petPortraitBox is Rectangle pet) { pet.Offset(dx, dy); menu.petPortraitBox = pet; }
            foreach (var picker in new[] { menu.eyeColorPicker, menu.hairColorPicker, menu.pantsColorPicker })
            {
                if (picker == null) continue;
                var field = AccessTools.Field(typeof(ColorPicker), "bounds");
                var bounds = (Rectangle)field.GetValue(picker);
                bounds.Offset(dx, dy); field.SetValue(picker, bounds);
            }
            menu.xPositionOnScreen = x; menu.yPositionOnScreen = y;
            menu.width = 660; menu.height = 660;
            menu.advancedOptionsButton.bounds.Location = new Point(x + 464, y + 568);
            LayoutFarms(menu);
            var back = new Rectangle(x + 686, y + 584, 198, 81);
            menu.backButton.bounds = back;
            if (Game1.activeClickableMenu is TitleMenu title) title.backButton.bounds = back;
        }

        private static void LayoutFarms(CharacterCustomization __instance)
        {
            if (!Applies(__instance)) return;
            int x = (Game1.uiViewport.Width - 900) / 2 + 688;
            int y = (Game1.uiViewport.Height - 660) / 2 + 40;
            for (int i = 0; i < __instance.farmTypeButtons.Count; i++)
                __instance.farmTypeButtons[i].bounds.Location = new Point(x + i / 6 * 96, y + i % 6 * 88);
            if (__instance.farmTypePreviousPageButton != null)
                __instance.farmTypePreviousPageButton.bounds.Location = new Point(x, y + 528);
            if (__instance.farmTypeNextPageButton != null)
                __instance.farmTypeNextPageButton.bounds.Location = new Point(x + 128, y + 528);
        }
    }
}
