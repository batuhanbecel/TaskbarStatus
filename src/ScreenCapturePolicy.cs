using System;
using System.Drawing;

namespace TaskbarStatus
{
    static class ScreenCapturePolicy
    {
        internal static bool PreservePresentation(string processName, Rectangle window, Rectangle screen)
        {
            // Older Windows uses a dedicated host, whose foreground toolbar may be
            // smaller than the screen. Modern Snipping Tool also has a normal editor:
            // only its screen-covering capture surface should suspend presentation.
            if (String.Equals(processName, "ScreenClippingHost", StringComparison.OrdinalIgnoreCase)) return true;
            return String.Equals(processName, "SnippingTool", StringComparison.OrdinalIgnoreCase)
                && window.Contains(screen);
        }
    }
}
