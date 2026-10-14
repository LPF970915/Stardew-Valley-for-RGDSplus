using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    internal static class ShopMenuLayout
    {
        internal static float FitScale(float requestedScale, int panelWidth, int panelHeight, int borderWidth)
        {
            const int physicalEdgeMargin = 12;
            float widthLimit = panelWidth / (float)(1000 + borderWidth * 2 + 120 + physicalEdgeMargin * 2);
            float heightLimit = panelHeight / (float)(600 + borderWidth * 2 + 16);
            return System.Math.Min(ScreenGeometry.ValidScale(requestedScale), System.Math.Min(widthLimit, heightLimit));
        }

        internal static void Refresh(ShopMenu menu)
        {
            UiPatches.EnsureUiViewport();
            var bounds = new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
            menu.gameWindowSizeChanged(bounds, bounds);
        }

        internal static void RefreshAfterShop(IClickableMenu newMenu)
        {
            UiPatches.EnsureUiViewport();
            if (newMenu == null) return;
            var bounds = new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
            newMenu.gameWindowSizeChanged(bounds, bounds);
        }
    }
}
