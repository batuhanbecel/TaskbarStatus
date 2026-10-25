using System;
using System.Drawing;

namespace TaskbarStatus
{
    static class OverlayLayout
    {
        internal static Rectangle KeepOnScreen(Rectangle screen, Rectangle desired)
        {
            int width = Math.Min(desired.Width, screen.Width);
            int height = Math.Min(desired.Height, screen.Height);
            int left = Math.Max(screen.Left, Math.Min(desired.Left, screen.Right - width));
            int top = Math.Max(screen.Top, Math.Min(desired.Top, screen.Bottom - height));
            return new Rectangle(left, top, width, height);
        }
    }
}
