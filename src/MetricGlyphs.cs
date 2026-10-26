using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskbarStatus
{
    enum MetricGlyph { None, Cpu, Ram, Gpu, Fps, Temperature, Download, Upload }

    // Small monochrome glyphs, independent of the installed icon fonts.
    static class MetricGlyphs
    {
        internal static MetricGlyph ForLabel(string label)
        {
            switch (label)
            {
                case "CPU": return MetricGlyph.Cpu;
                case "RAM": return MetricGlyph.Ram;
                case "GPU": return MetricGlyph.Gpu;
                case "FPS": return MetricGlyph.Fps;
                case "↓": return MetricGlyph.Download;
                case "↑": return MetricGlyph.Upload;
                default: return MetricGlyph.None;
            }
        }

        internal static void Draw(IntPtr dc, MetricGlyph glyph, int x, int y, int size)
        {
            using (Graphics g = Graphics.FromHdc(dc))
            {
                g.TranslateTransform(x, y);
                g.ScaleTransform(size / 16f, size / 16f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(Color.White, 1.4f))
                {
                    switch (glyph)
                    {
                        case MetricGlyph.Cpu:
                            g.DrawRectangle(p, 4, 4, 8, 8);
                            for (int n = 5; n <= 11; n += 3)
                            {
                                g.DrawLine(p, n, 1, n, 4); g.DrawLine(p, n, 12, n, 15);
                                g.DrawLine(p, 1, n, 4, n); g.DrawLine(p, 12, n, 15, n);
                            }
                            break;
                        case MetricGlyph.Ram:
                            g.DrawRectangle(p, 1, 4, 14, 7);
                            for (int n = 3; n <= 12; n += 3)
                            {
                                g.DrawLine(p, n, 6, n, 9); g.DrawLine(p, n, 11, n, 14);
                            }
                            break;
                        case MetricGlyph.Gpu:
                            g.DrawRectangle(p, 1, 2, 14, 10);
                            g.DrawLine(p, 8, 12, 8, 15); g.DrawLine(p, 4, 15, 12, 15);
                            break;
                        case MetricGlyph.Fps:
                            g.DrawArc(p, 2, 3, 12, 12, 180, 180);
                            g.DrawLine(p, 8, 9, 12, 5); g.DrawLine(p, 2, 12, 14, 12);
                            break;
                        case MetricGlyph.Temperature:
                            g.DrawArc(p, 6, 1, 4, 4, 180, 180);
                            g.DrawLine(p, 6, 3, 6, 9); g.DrawLine(p, 10, 3, 10, 9);
                            g.DrawArc(p, 4, 7, 8, 8, -60, 300);
                            g.DrawLine(p, 8, 5, 8, 12);
                            break;
                        case MetricGlyph.Download:
                        case MetricGlyph.Upload:
                            bool down = glyph == MetricGlyph.Download;
                            int end = down ? 12 : 3, tail = down ? 3 : 12, wing = down ? 8 : 7;
                            g.DrawLine(p, 8, tail, 8, end);
                            g.DrawLines(p, new[] { new Point(4, wing), new Point(8, end), new Point(12, wing) });
                            g.DrawLine(p, 3, 15, 13, 15);
                            break;
                    }
                }
            }
        }
    }
}
