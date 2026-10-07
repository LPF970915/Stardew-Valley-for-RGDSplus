using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class DisplayModeController
    {
        internal static readonly DisplayModeState State = new DisplayModeState();
        private static IModHelper helper;
        private static readonly HashSet<SButton> releaseGate = new HashSet<SButton>();
        private static readonly HashSet<SButton> diagnosedButtons = new HashSet<SButton>();
        private static readonly SButton[] allButtons = (SButton[])Enum.GetValues(typeof(SButton));
        private static readonly List<SButton> released = new List<SButton>();
        private static IClickableMenu ownedPage;
        private static int selectedPage;
        private static DashboardMenu panel;
        internal static DashboardMenu Panel => panel ??= new DashboardMenu(selectedPage);
        internal static int SelectedPage => Panel.SelectedPage;
        private static long suppressSyntheticMouseUntil;
        private static int activeTouchId = -1;
        private static Vector2 activeTouchPosition;
        private static long activeTouchStarted;
        private static bool touchPollFailed;
        private static long lastModeToggleTicks;
        private static bool returnToDashboard;
        private static Vector2 evdevTouchPosition;
        private static bool evdevTouchDown;
        internal static float MenuScaleLimit = float.MaxValue;
        internal static bool Expanded => VerticalWorldRenderer.Enabled && State.Mode == LowerScreenMode.ExpandedHud
            && Game1.gameMode == 3 && Game1.currentLocation != null && Game1.currentMinigame == null
            && Game1.game1?.takingMapScreenshot != true;

        internal static void Initialize(IModHelper modHelper)
        {
            helper = modHelper;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.ReturnedToTitle += (_, __) => Reset();
        }
        private static void Reset()
        {
            State.Reset();
            ownedPage = null;
            panel = null;
            selectedPage = 0;
            releaseGate.Clear();
            diagnosedButtons.Clear();
            MenuScaleLimit = float.MaxValue;
            suppressSyntheticMouseUntil = 0;
            activeTouchId = -1;
            activeTouchPosition = Vector2.Zero;
            activeTouchStarted = 0;
            touchPollFailed = false;
            lastModeToggleTicks = 0;
            returnToDashboard = false;
            evdevTouchDown = false;
            Dashboard.Reset();
        }
        private static void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!VerticalWorldRenderer.Enabled || !Context.IsWorldReady) return;
            DiagnoseCandidateButton(e);
            if (releaseGate.Contains(e.Button)) { helper.Input.Suppress(e.Button); return; }
            if (IsModeSwitchButton(e.Button))
            {
                // RGDSplus exposes physical Select as evdev code 310 and, when SDL owns
                // the D2 entry, as ControllerBack or Back.
                helper.Input.Suppress(e.Button);
                if (!TryEnterModeSwitch()) return;
                return;
            }
            if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
            {
                // Some RGDSplus firmware versions expose one physical touch as
                // both TouchPanel input and a delayed MouseLeft event. The
                // TouchPanel path is authoritative for the lower panel.
                if (Environment.TickCount64 <= suppressSyntheticMouseUntil)
                {
                    helper.Input.Suppress(e.Button);
                    return;
                }
                // Native menus already receive SDL mouse clicks. Only the custom
                // dashboard needs explicit dispatch; evdev is handled separately.
                if (e.Button == SButton.MouseLeft && (Expanded && Game1.activeClickableMenu == null
                    || Game1.activeClickableMenu is DashboardMenu) && TryGetLowerPanelPoint(e.Cursor, out var point))
                {
                    helper.Input.Suppress(e.Button);
                    if (Game1.activeClickableMenu is DashboardMenu mousePanel) mousePanel.receiveLeftClick(point.X, point.Y);
                    else Panel.receiveLeftClick(point.X, point.Y);
                }
                return;
            }
            if (!Expanded) return;
            if (e.Button == SButton.ControllerStart)
            {
                helper.Input.Suppress(e.Button);
                return;
            }
            if (Game1.activeClickableMenu is DashboardMenu dashboard && dashboard.HandleButton(e.Button))
            {
                helper.Input.Suppress(e.Button);
                return;
            }
            if (Game1.activeClickableMenu == null && !Game1.dialogueUp && !Game1.eventUp)
            {
                if (e.Button == SButton.ControllerY || e.Button == SButton.J || e.Button == SButton.M)
                {
                    helper.Input.Suppress(e.Button);
                    OpenPage(e.Button == SButton.ControllerY ? 1 : e.Button == SButton.J ? 4 : 3);
                    return;
                }
                if ((e.Button == SButton.LeftShoulder || e.Button == SButton.RightShoulder) && !Game1.player.UsingTool)
                {
                    helper.Input.Suppress(e.Button);
                    Game1.player.shiftToolbar(e.Button == SButton.RightShoulder);
                }
            }
        }

        private static bool IsModeSwitchButton(SButton button) =>
            // RGDSplus D2 Card2 reports the physical Select as evdev 310. Keep only
            // the SDL/back-key aliases used by older launchers.
            button == SButton.BigButton || button == SButton.ControllerBack
                || button == SButton.Back;

        private static void DiagnoseCandidateButton(ButtonPressedEventArgs e)
        {
            if (e.Button != SButton.ControllerBack && e.Button != SButton.ControllerStart
                && e.Button != SButton.Back && e.Button != SButton.BigButton) return;
            if (!diagnosedButtons.Add(e.Button)) return;
            ModEntry.Write("input-candidate button=" + e.Button + "; mode=" + State.Mode
                + "; world=" + Context.IsWorldReady + "; gameMode=" + Game1.gameMode
                + "; menu=" + (Game1.activeClickableMenu?.GetType().Name ?? "none")
                + "; expanded=" + Expanded);
        }

        private static void OnMouseWheelScrolled(object sender, MouseWheelScrolledEventArgs e)
        {
            if (!Expanded) return;
            var menu = Game1.activeClickableMenu;
            if (menu == null) { Panel.receiveScrollWheelAction(e.Delta); return; }
            if (menu is DashboardMenu)
            {
                menu.receiveScrollWheelAction(e.Delta);
            }
        }

        private static bool TryGetLowerPanelPoint(ICursorPosition cursor, out Point point)
        {
            point = Point.Zero;
            if (cursor == null) return false;
            Vector2 raw = cursor.ScreenPixels;
            return TryGetLowerPanelPoint(raw, out point);
        }

        private static bool TryGetLowerPanelPoint(Vector2 raw, out Point point)
        {
            return LowerPanelGeometry.TryMap(raw.X, raw.Y, Game1.uiViewport.Width, Game1.uiViewport.Height, out point);
        }
        internal static bool CanSwitchMenu(IClickableMenu menu)
        {
            if (menu == null || menu is DialogueBox) return true;
            if (!(menu is DashboardMenu || menu is GameMenu || menu is QuestLog || ReferenceEquals(menu, ownedPage))) return false;
            for (var current = menu; current != null; current = current.GetChildMenu())
                if (!current.readyToClose()) return false;
            return true;
        }
        internal static void GateHeldButtons()
        {
            if (helper == null) return;
            foreach (var button in allButtons)
                if (button != SButton.None && helper.Input.IsDown(button))
                {
                    releaseGate.Add(button);
                    helper.Input.Suppress(button);
                }
        }
        private static void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Suppression lasts until release: an A/B/Select held across a transition cannot reach the new owner.
            released.Clear();
            foreach (var button in releaseGate)
                if (helper.Input.IsDown(button)) helper.Input.Suppress(button);
                else released.Add(button);
            foreach (var button in released) releaseGate.Remove(button);
            // Poll touch independently of the world presentation mode. Native
            // pages can remain active for a few ticks while the mode flag is
            // changing, and gating this behind Expanded drops those presses.
            EvdevInput.Poll(HandleEvdevKey, HandleEvdevTouch);
            PumpTouchInput();
            if (returnToDashboard)
            {
                returnToDashboard = false;
                if (Expanded && Game1.activeClickableMenu == null) OpenDashboard();
            }
            if (!Expanded) return;
            if (e.IsMultipleOf(60)) Dashboard.RefreshSummary();
        }

        private static void HandleEvdevKey(int code)
        {
            if (code == 310) TryEnterModeSwitch();
        }

        private static void HandleEvdevTouch(float x, float y, bool pressed, bool releasedTouch)
        {
            if (!LowerPanelGeometry.TryMap(x, y, Game1.uiViewport.Width, Game1.uiViewport.Height, out var point)) return;
            if (pressed)
            {
                evdevTouchDown = true;
                evdevTouchPosition = new Vector2(x, y);
                suppressSyntheticMouseUntil = Environment.TickCount64 + 450;
                ModEntry.Write("touch-pressed evdev raw=" + x + "," + y + "; mapped=" + point
                    + "; mode=" + State.Mode + "; menu=" + (Game1.activeClickableMenu?.GetType().Name ?? "none"));
                DispatchTouchPress(point);
            }
            else if (releasedTouch)
            {
                evdevTouchDown = false;
                if (Game1.activeClickableMenu != null) Game1.activeClickableMenu.releaseLeftClick(point.X, point.Y);
                else if (Expanded) Panel.releaseLeftClick(point.X, point.Y);
                suppressSyntheticMouseUntil = Environment.TickCount64 + 250;
            }
            else if (evdevTouchDown)
            {
                suppressSyntheticMouseUntil = Environment.TickCount64 + 450;
                Game1.activeClickableMenu?.leftClickHeld(point.X, point.Y);
                if (Game1.activeClickableMenu == null && Expanded) Panel.leftClickHeld(point.X, point.Y);
                float delta = y - evdevTouchPosition.Y;
                if (Math.Abs(delta) >= 28 && UsesTouchScroll(Game1.activeClickableMenu))
                {
                    DispatchTouchScroll(delta < 0 ? -1 : 1);
                    evdevTouchPosition = new Vector2(x, y);
                }
            }
        }

        private static bool TryEnterModeSwitch()
        {
            var now = Environment.TickCount64;
            if (now - lastModeToggleTicks < 300) return false;
            lastModeToggleTicks = now;
            var menu = Game1.activeClickableMenu;
            bool canSwitch = Game1.gameMode == 3 && Game1.currentMinigame == null && !Game1.eventUp
                && !Game1.fadeToBlack && !Game1.globalFade && !Game1.player.UsingTool
                && CanSwitchMenu(menu);
            if (!State.TryToggle(canSwitch)) return false;
            ownedPage = null;
            MenuScaleLimit = float.MaxValue;
            if (menu != null && !(menu is DialogueBox)) menu.exitThisMenu(false);
            Game1.player.Halt();
            GateHeldButtons();
            UiPatches.EnsureViewports();
            VerticalWorldRenderer.InvalidateWorld();
            ModEntry.Write("mode=" + State.Mode + "; generation=" + State.Generation);
            Game1.playSound("smallSelect");
            return true;
        }

        private static void PumpTouchInput()
        {
            TouchCollection touches;
            try
            {
                touches = TouchPanel.GetState();
            }
            catch (Exception ex)
            {
                if (!touchPollFailed)
                {
                    touchPollFailed = true;
                    ModEntry.Write("touch-poll-failed " + ex.GetType().Name + ": " + ex.Message);
                }
                return;
            }

            foreach (var touch in touches)
            {
                if (touch.State == TouchLocationState.Pressed)
                {
                    if (!TryGetLowerPanelPoint(touch.Position, out var point)) continue;
                    activeTouchId = touch.Id;
                    activeTouchPosition = touch.Position;
                    activeTouchStarted = Environment.TickCount64;
                    suppressSyntheticMouseUntil = activeTouchStarted + 450;
                    ModEntry.Write("touch-pressed raw=" + touch.Position + "; mapped=" + point
                        + "; menu=" + (Game1.activeClickableMenu?.GetType().Name ?? "none"));
                    DispatchTouchPress(point);
                }
                else if (touch.Id == activeTouchId && touch.State == TouchLocationState.Moved)
                {
                    if (TryGetLowerPanelPoint(touch.Position, out var point))
                    {
                        Game1.activeClickableMenu?.leftClickHeld(point.X, point.Y);
                        if (Game1.activeClickableMenu == null && Expanded) Panel.leftClickHeld(point.X, point.Y);
                    }
                    float deltaY = touch.Position.Y - activeTouchPosition.Y;
                    if (Math.Abs(deltaY) >= 28f && UsesTouchScroll(Game1.activeClickableMenu))
                    {
                        DispatchTouchScroll(deltaY < 0 ? -1 : 1);
                        activeTouchPosition = touch.Position;
                        suppressSyntheticMouseUntil = Environment.TickCount64 + 450;
                    }
                }
                else if (touch.Id == activeTouchId && touch.State == TouchLocationState.Released)
                {
                    if (TryGetLowerPanelPoint(touch.Position, out var point))
                    {
                        if (Game1.activeClickableMenu != null) Game1.activeClickableMenu.releaseLeftClick(point.X, point.Y);
                        else if (Expanded) Panel.releaseLeftClick(point.X, point.Y);
                    }
                    activeTouchId = -1;
                    suppressSyntheticMouseUntil = Environment.TickCount64 + 250;
                }
            }
        }

        private static void DispatchTouchPress(Point point)
        {
            if (Game1.activeClickableMenu is DashboardMenu dashboard)
            {
                dashboard.ReceiveTouch(point.X, point.Y);
                return;
            }
            if (Game1.activeClickableMenu != null && !(Game1.activeClickableMenu is DialogueBox))
            {
                Game1.activeClickableMenu.receiveLeftClick(point.X, point.Y, true);
                return;
            }
            if (Game1.activeClickableMenu == null && !Game1.dialogueUp && !Game1.eventUp)
            {
                if (Expanded) OpenDashboardAt(point);
                else if (Context.IsWorldReady)
                    foreach (var menu in Game1.onScreenMenus)
                        if (menu is Toolbar) menu.receiveLeftClick(point.X, point.Y, true);
            }
        }

        private static bool UsesTouchScroll(IClickableMenu menu) => menu is DashboardMenu || menu is QuestLog
            || menu == null && Expanded
            || menu is GameMenu native && (native.currentTab == GameMenu.socialTab
                || native.currentTab == GameMenu.optionsTab || native.currentTab == GameMenu.craftingTab);

        private static void DispatchTouchScroll(int direction)
        {
            var menu = Game1.activeClickableMenu;
            if (menu == null && Expanded) { Panel.receiveScrollWheelAction(direction); return; }
            if (UsesTouchScroll(menu))
                menu.receiveScrollWheelAction(direction);
        }
        private static void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu == null && (e.OldMenu is DashboardMenu || ReferenceEquals(e.OldMenu, ownedPage))) GateHeldButtons();
            if (!ReferenceEquals(e.OldMenu, ownedPage) || ownedPage == null) return;
            if (e.NewMenu is GameMenu && e.OldMenu is GameMenu) { ownedPage = e.NewMenu; return; }
            if (e.NewMenu == null && Expanded) returnToDashboard = true;
            ownedPage = null;
            MenuScaleLimit = float.MaxValue;
        }
        private static void OpenDashboard()
        {
            MenuScaleLimit = float.MaxValue;
            UiPatches.EnsureUiViewport();
            panel = new DashboardMenu(selectedPage);
        }
        internal static void TestTouch(float x, float y, bool pressed, bool releasedTouch) => HandleEvdevTouch(x, y, pressed, releasedTouch);
        internal static void TestToggle() => TryEnterModeSwitch();
        internal static void TestDashboard() { selectedPage = 0; OpenDashboard(); DashboardPages.Select(0); }
        private static void OpenDashboardAt(Point point)
        {
            Panel.ReceiveTouch(point.X, point.Y);
        }
        internal static void OpenPage(int index)
        {
            if (!CompanionPreferences.Visible(index)) index = 0;
            selectedPage = index;
            Panel.SelectPage(index);
            DashboardPages.Select(index);
        }
    }
}
