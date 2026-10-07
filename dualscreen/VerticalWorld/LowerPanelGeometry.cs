using Microsoft.Xna.Framework;

namespace Rgdsplus.VerticalWorld
{
    internal static class LowerPanelGeometry
    {
        internal const int PhysicalWidth = 1024;
        internal const int PhysicalHeight = 768;

        // Touch devices report the panel either as local 0..1023 coordinates or as
        // the shared 2048x768 compositor window. Both forms map to the same UI.
        internal static bool TryMap(float x, float y, int uiWidth, int uiHeight, out Point point)
        {
            point = Point.Zero;
            if (x >= PhysicalWidth) x -= PhysicalWidth;
            if (x < 0 || y < 0 || x >= PhysicalWidth || y >= PhysicalHeight) return false;
            if (uiWidth <= 0 || uiHeight <= 0) return false;
            point = new Point(
                (int)(x * uiWidth / PhysicalWidth),
                (int)(y * uiHeight / PhysicalHeight));
            return true;
        }
    }
}
