using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal sealed class DashboardMenu : IClickableMenu
    {
        private int selection;
        private int pressedSlot = -1;
        private Item pressedItem;
        private long pressedAt;
        private Point pressedPoint;
        private bool pendingTap;
        private Point lastPoint;
        private Point gestureStart;
        private bool navigationGesture;
        private bool touchDown;
        private bool navigationMoved;
        internal int SelectedPage => selection;
        internal void SelectPage(int page) { selection = page; Dashboard.Reveal(page); }
        internal DashboardMenu(int page = 0) : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height)
        {
            selection = page;
            Dashboard.Notice = "";
            Dashboard.RefreshSummary();
        }
        public override bool areGamePadControlsImplemented() => true;
        public override bool showWithoutTransparencyIfOptionIsSet() => false;
        internal bool HandleButton(SButton button)
        {
            switch (button)
            {
                case SButton.DPadUp: case SButton.LeftThumbstickUp: case SButton.Up: case SButton.W:
                    selection = Dashboard.Adjacent(selection, -1); OpenSelection(); return true;
                case SButton.DPadDown: case SButton.LeftThumbstickDown: case SButton.Down: case SButton.S:
                    selection = Dashboard.Adjacent(selection, 1); OpenSelection(); return true;
                case SButton.ControllerA: case SButton.Enter: case SButton.Space:
                    OpenSelection(); return true;
                case SButton.ControllerB: case SButton.ControllerStart: case SButton.Escape:
                    exitThisMenu(); DisplayModeController.GateHeldButtons(); return true;
                case SButton.RightTrigger: case SButton.PageDown:
                    DashboardPages.Scroll(selection, -1); return true;
                case SButton.LeftTrigger: case SButton.PageUp:
                    DashboardPages.Scroll(selection, 1); return true;
                case SButton.DPadLeft: case SButton.DPadRight: case SButton.LeftThumbstickLeft: case SButton.LeftThumbstickRight:
                    return true;
                default: return false;
            }
        }
        private void OpenSelection()
        {
            Dashboard.Notice = "";
            Dashboard.ScrollRow = 0;
            DisplayModeController.OpenPage(selection);
            Game1.playSound("smallSelect");
        }
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (Dashboard.Bounds(CompanionLayout.PreviousPage).Contains(x,y)) { selection=Dashboard.Adjacent(selection,-1); OpenSelection(); return; }
            if (Dashboard.Bounds(CompanionLayout.NextPage).Contains(x,y)) { selection=Dashboard.Adjacent(selection,1); OpenSelection(); return; }
            for (int i = 0; i < Dashboard.PageCount; i++)
                if (Dashboard.NavBounds(i).Contains(x, y)) { selection = i; OpenSelection(); return; }
            if (DashboardPages.Click(selection, x, y)) return;
            int item = Dashboard.HitItem(x, y, selection == 0);
            if (item >= 0 && !Game1.player.UsingTool)
            {
                Game1.player.CurrentToolIndex = item;
                if (playSound) Game1.playSound("smallSelect");
            }
        }
        internal void ReceiveTouch(int x, int y)
        {
            pressedPoint = new Point(x, y);
            touchDown = true;
            gestureStart = lastPoint = Dashboard.Physical(x,y);
            navigationGesture = gestureStart.Y < 90;
            navigationMoved = false;
            DashboardPages.Pointer(x,y);
            pendingTap = true;
            pressedSlot = Dashboard.HitItem(x, y, selection == 0);
            pressedItem = pressedSlot >= 0 && pressedSlot < Game1.player.Items.Count ? Game1.player.Items[pressedSlot] : null;
            pressedAt = System.Environment.TickCount64;
            if (pressedSlot >= 0 && !Game1.player.UsingTool)
                Game1.player.CurrentToolIndex = pressedSlot;
        }
        public override void leftClickHeld(int x, int y)
        {
            if (System.Math.Abs(x - pressedPoint.X) > Game1.uiViewport.Width * .012f
                || System.Math.Abs(y - pressedPoint.Y) > Game1.uiViewport.Height * .012f) pendingTap = false;
            var point=Dashboard.Physical(x,y);
            if(selection==3 && CompanionLayout.MapCanvas.Contains(gestureStart) && !navigationGesture)
                DashboardPages.DragMap(point.X-lastPoint.X,point.Y-lastPoint.Y);
            if(navigationGesture && !navigationMoved && System.Math.Abs(point.X-gestureStart.X)>=80)
            {
                selection=Dashboard.Adjacent(selection,point.X<gestureStart.X?1:-1);
                OpenSelection(); navigationMoved=true; pendingTap=false;
            }
            else if(pressedSlot<0 && System.Math.Abs(point.X-gestureStart.X)>=70
                && DashboardPages.Swipe(selection,gestureStart,point.X<gestureStart.X?1:-1))
            { gestureStart=point; pendingTap=false; }
            lastPoint=point;
        }
        public override void releaseLeftClick(int x, int y)
        {
            leftClickHeld(x, y);
            touchDown = false;
            int from = pressedSlot;
            pressedSlot = -1;
            bool tap = pendingTap;
            pendingTap = false;
            if (from < 0)
            {
                if (tap) receiveLeftClick(pressedPoint.X, pressedPoint.Y, true);
                return;
            }
            if (from < 0 || from >= Game1.player.Items.Count || Game1.player.UsingTool || !Game1.player.CanMove
                || Game1.activeClickableMenu != null || !ReferenceEquals(pressedItem, Game1.player.Items[from])) return;
            int to = Dashboard.HitItem(x, y, selection == 0);
            if (to >= 0 && to < Game1.player.Items.Count && from != to)
            {
                (Game1.player.Items[from], Game1.player.Items[to]) = (Game1.player.Items[to], Game1.player.Items[from]);
                Game1.player.CurrentToolIndex = to; Game1.playSound("smallSelect");
            }
            else if (from == to && System.Environment.TickCount64 - pressedAt >= 650
                && pressedItem is StardewValley.Object food && food.Edibility != -300)
            {
                Game1.player.eatObject(food);
                if (Game1.player.isEating) Game1.player.reduceActiveItemByOne();
            }
        }
        public override void receiveScrollWheelAction(int direction)
        {
            pendingTap = false;
            if (touchDown && (navigationGesture || selection==3 && CompanionLayout.MapCanvas.Contains(gestureStart))) return;
            DashboardPages.Scroll(selection, direction);
        }
        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            xPositionOnScreen = yPositionOnScreen = 0;
            width = Game1.uiViewport.Width; height = Game1.uiViewport.Height;
        }
        public override void draw(SpriteBatch b)
        {
            Dashboard.Draw(b, selection, true);
            if (!Game1.options.gamepadControls) drawMouse(b);
        }
    }
}
