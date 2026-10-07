using System;

namespace Rgdsplus.VerticalWorld
{
    // No game dependencies: the camera and scaling contract can be tested offline.
    internal static class ScreenGeometry
    {
        internal const int PanelWidth = 1024;
        internal const int PanelHeight = 768;
        internal static float ValidScale(float scale) => float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0 ? 1f : scale;
        internal static int Width(float scale) => (int)Math.Ceiling(PanelWidth / (double)ValidScale(scale));
        internal static int Height(float scale) => (int)Math.Ceiling(PanelHeight / (double)ValidScale(scale));
        internal static int CameraX(float playerX, int mapWidth, int width) => Clamp((int)playerX + 32 - width / 2, mapWidth - width);
        internal static int CameraY(float playerY, int mapHeight, int panelHeight, bool extendLower = true) => Clamp((int)playerY + 32 - panelHeight / 2, mapHeight - (extendLower ? 2 : 1) * panelHeight);
        private static int Clamp(int value, int maximum) => Math.Max(0, Math.Min(Math.Max(0, maximum), value));
    }
}
