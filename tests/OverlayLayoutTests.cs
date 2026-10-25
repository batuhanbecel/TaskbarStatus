using System;
using System.Drawing;
using TaskbarStatus;

static class OverlayLayoutTests
{
    static void Check(string name, Rectangle screen, Rectangle desired, Rectangle expected)
    {
        Rectangle actual = OverlayLayout.KeepOnScreen(screen, desired);
        if (actual != expected) throw new Exception(name + ": " + actual + " != " + expected);
        Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        Check("Visible taskbar", screen, new Rectangle(1200, 1032, 500, 48), new Rectangle(1200, 1032, 500, 48));
        Check("Hidden bottom taskbar", screen, new Rectangle(1200, 1078, 500, 48), new Rectangle(1200, 1032, 500, 48));
        Check("Hidden top taskbar", screen, new Rectangle(1200, -46, 500, 48), new Rectangle(1200, 0, 500, 48));
        Check("Stale bounds after resolution change", new Rectangle(0, 0, 1280, 720),
            new Rectangle(1400, 1080, 500, 48), new Rectangle(780, 672, 500, 48));
        Check("Negative monitor origin", new Rectangle(-1920, -1080, 1920, 1080),
            new Rectangle(-600, -2, 500, 48), new Rectangle(-600, -48, 500, 48));
        Check("Large drag offset", screen, new Rectangle(-4000, 1032, 500, 48), new Rectangle(0, 1032, 500, 48));
        Check("Overlay wider than screen", screen, new Rectangle(-500, 1032, 2400, 48), new Rectangle(0, 1032, 1920, 48));
    }
}
