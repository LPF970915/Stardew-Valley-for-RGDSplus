using System;
using Microsoft.Xna.Framework;

namespace Rgdsplus.VerticalWorld
{
    // Physical 1024 x 768 reference canvas, designed at 150% UI scale.
    internal static class CompanionLayout
    {
        internal const int Width = 1024, Height = 768, Pages = 12;
        internal static Rectangle Content => new Rectangle(16, 90, 992, 630);
        internal static Rectangle PreviousPage => new Rectangle(16, 16, 56, 60);
        internal static Rectangle NextPage => new Rectangle(952, 16, 56, 60);
        internal static Rectangle Tab(int index) => new Rectangle(84 + index * 288, 16, 280, 60);
        internal static Rectangle Inventory(int index) => new Rectangle(196 + index % 12 * 68, 174 + index / 12 * 96, 64, 64);
        internal static Rectangle Sort => new Rectangle(824, 98, 176, 48);
        internal static Rectangle PreviousRow => new Rectangle(196, 444, 56, 44);
        internal static Rectangle NextRow => new Rectangle(944, 444, 56, 44);
        internal static Rectangle ListRow(int row) => new Rectangle(24, 158 + row * 78, 300, 72);
        internal static Rectangle MapCanvas => new Rectangle(24, 96, 976, 490);
        internal static Rectangle Calendar(int day) => new Rectangle(24 + (day - 1) % 7 * 140, 166 + (day - 1) / 7 * 120, 136, 116);
        internal static Rectangle Scale(Rectangle box, int width, int height) => new Rectangle(
            (int)Math.Round(box.X * width / 1024f), (int)Math.Round(box.Y * height / 768f),
            (int)Math.Round(box.Width * width / 1024f), (int)Math.Round(box.Height * height / 768f));
    }
}
