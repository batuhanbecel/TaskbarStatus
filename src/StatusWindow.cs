using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace TaskbarStatus
{
    // A borderless, per-pixel-alpha, topmost window that sits on the taskbar just left of the tray.
    class StatusWindow : Form
    {
        const string AppName = "TaskbarStatus";

        readonly Settings settings = Settings.Load();
        readonly CpuMonitor cpu = new CpuMonitor();
        readonly CpuTempMonitor cpuTemp = new CpuTempMonitor();
        readonly GpuMonitor gpu = new GpuMonitor();
        readonly GpuSensor gpuSensor = new GpuSensor();
        readonly FpsMonitor fps = new FpsMonitor();
        readonly NetMonitor net = new NetMonitor();
        readonly Timer timer = new Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolTip tip = new ToolTip();
        readonly List<Action> refreshChecks = new List<Action>();
        readonly Updater updater = new Updater();
        DateTime nextUpdateCheck = DateTime.UtcNow.AddSeconds(15);
        volatile string pendingInstaller;   // downloaded, verified installer waiting to run

        // Embedding into the taskbar (see SetEmbedded).
        IntPtr embeddedIn;
        bool embedFailed, closing;
        Form menuOwner;
        public static bool RestartRequested;
        int updateBusy;
        bool startupEnabled;

        // Latest readings.
        float cpuUsage, ramUsage;
        double ramUsedGb, ramTotalGb;
        float? gpuUsage, cpuTempValue;
        int fpsValue, fpsPid;
        DateTime nextCpuTempRead;
        int cpuTempBusy;

        // Text rendering (GDI, ClearType natural quality, like the taskbar clock).
        IntPtr measureDc, font;
        string fontKey;
        int fontHeight;
        bool hidden;

        // Dragging horizontally along the taskbar.
        bool mouseDown, dragging;
        int dragStartX, dragStartOffset;

        public StatusWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = AppName;
            measureDc = CreateCompatibleDC(IntPtr.Zero);
            tip.ShowAlways = true;
            tip.InitialDelay = 400;
            tip.AutoPopDelay = 30000;
            startupEnabled = Autostart.IsEnabled();
            L.Apply(settings.Language);
            BuildMenu();
            menu.Opening += (s, e) =>
            {
                tip.Hide(this);   // otherwise the tooltip stays on top of the menu
                foreach (Action a in refreshChecks) a();
            };
            timer.Tick += (s, e) => Tick();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySettings();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        // As a child of the taskbar, the widget is destroyed with it when Explorer restarts.
        // Program then starts a fresh instance, which waits for the new taskbar.
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (!closing && !RecreatingHandle) RestartRequested = true;
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            fps.Dispose();
            gpuSensor.Dispose();
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(measureDc);
            base.OnFormClosed(e);
        }

        void ApplySettings()
        {
            settings.Save();
            timer.Interval = settings.Interval;
            timer.Stop();
            Tick();
            timer.Start();
        }

        // --- sampling ---

        void Tick()
        {
            cpuUsage = cpu.Read();
            ramUsage = RamMonitor.Read(out ramUsedGb, out ramTotalGb);
            gpuUsage = gpu.Read();
            gpuSensor.Update();
            if (settings.ShowNet) net.Update();
            // WMI can take seconds when the sensor tool is not running, so stay off the UI thread.
            if (settings.ShowCpuTemp && DateTime.UtcNow >= nextCpuTempRead &&
                Interlocked.CompareExchange(ref cpuTempBusy, 1, 0) == 0)
            {
                nextCpuTempRead = DateTime.UtcNow.AddSeconds(2);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { cpuTempValue = cpuTemp.Read(); }
                    finally { cpuTempBusy = 0; }
                });
            }

            IntPtr fg = GetForegroundWindow();
            if (fps.Available)
            {
                fpsValue = fps.Read();
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                fpsPid = (int)pid;
                fps.TargetPid = fpsPid;
            }

            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            bool fullscreen = tray != IntPtr.Zero && IsFullscreen(fg, tray);
            bool shouldHide = tray == IntPtr.Zero || (!settings.ShowInFullscreen && fullscreen);
            if (shouldHide)
            {
                if (!hidden) { ShowWindow(Handle, SW_HIDE); hidden = true; }
                return;
            }
            if (hidden) { ShowWindow(Handle, SW_SHOWNOACTIVATE); hidden = false; }

            // The taskbar hides itself behind fullscreen apps, and its children with it.
            SetEmbedded(tray, !fullscreen);
            Render(tray, fullscreen);
            if (!menu.Visible && ClientRectangle.Contains(PointToClient(Cursor.Position))) tip.SetToolTip(this, Details());
            if (settings.CheckUpdates && !Program.Dev && DateTime.UtcNow >= nextUpdateCheck) ScheduleUpdateCheck(false);
            if (!fullscreen) RunPendingInstaller();
        }

        // --- what to show ---

        enum Level { Normal, Warn, Hot }

        class Cell
        {
            public string Label;
            public List<string> Values = new List<string>();
            public List<string> Widest = new List<string>();   // fixes the width so text does not jitter
            public List<Level> Levels = new List<Level>();

            public Cell(string label) { Label = label; }

            public void Add(string value, string widest, Level level)
            {
                Values.Add(value);
                Widest.Add(widest);
                Levels.Add(level);
            }
        }

        List<Cell> BuildCells()
        {
            var cells = new List<Cell>();
            if (settings.ShowCpu || settings.ShowCpuTemp)
            {
                var c = new Cell("CPU");
                if (settings.ShowCpu) c.Add(Percent(cpuUsage), "100%", UsageLevel(cpuUsage));
                if (settings.ShowCpuTemp) c.Add(Temp(cpuTempValue), "100°C", TempLevel(cpuTempValue));
                cells.Add(c);
            }
            if (settings.ShowRam)
            {
                var c = new Cell("RAM");
                if (settings.RamAsGb) c.Add(ramUsedGb.ToString("0.0") + " GB", "99.9 GB", UsageLevel(ramUsage));
                else c.Add(Percent(ramUsage), "100%", UsageLevel(ramUsage));
                cells.Add(c);
            }
            if (settings.ShowGpu || settings.ShowGpuTemp)
            {
                var c = new Cell("GPU");
                if (settings.ShowGpu) c.Add(gpuUsage.HasValue ? Percent(gpuUsage.Value) : "--", "100%", UsageLevel(gpuUsage ?? 0));
                if (settings.ShowGpuTemp) c.Add(Temp(gpuSensor.Temperature), "100°C", TempLevel(gpuSensor.Temperature));
                cells.Add(c);
            }
            if (settings.ShowFps)
            {
                var c = new Cell("FPS");
                c.Add(fps.Available && fpsValue > 0 ? fpsValue.ToString() : "--", "999", Level.Normal);
                cells.Add(c);
            }
            if (settings.ShowNet)
            {
                var down = new Cell("↓");
                down.Add(NetMonitor.Format(net.Down), "999 KB/s", Level.Normal);
                var up = new Cell("↑");
                up.Add(NetMonitor.Format(net.Up), "999 KB/s", Level.Normal);
                cells.Add(down);
                cells.Add(up);
            }
            if (cells.Count == 0)
            {
                var c = new Cell(AppName);
                cells.Add(c);
            }
            return cells;
        }

        static string Percent(float v) { return ((int)Math.Round(v)).ToString() + "%"; }

        static string Temp(float? v) { return v.HasValue ? ((int)Math.Round(v.Value)).ToString() + "°C" : "--"; }

        static Level UsageLevel(float v) { return v >= 90 ? Level.Warn : Level.Normal; }

        static Level TempLevel(float? v)
        {
            if (!v.HasValue) return Level.Normal;
            return v.Value >= 85 ? Level.Hot : v.Value >= 75 ? Level.Warn : Level.Normal;
        }

        string Details()
        {
            var sb = new StringBuilder();
            sb.Append("CPU: ").Append(Percent(cpuUsage));
            if (cpuTempValue.HasValue) sb.Append("  ·  ").Append(Temp(cpuTempValue)).Append(" (").Append(cpuTemp.Source).Append(")");
            else if (settings.ShowCpuTemp)
                sb.Append("  ·  ").Append(
                    !PawnIo.Installed ? L.T("temp: PawnIO is not installed", "sıcaklık: PawnIO kurulu değil")
                    : !Autostart.IsAdmin() ? L.T("temp: needs administrator rights", "sıcaklık: yönetici izni gerekli")
                    : L.T("temp: not supported on this CPU", "sıcaklık: bu işlemcide okunamadı"));
            sb.AppendLine();
            sb.Append("RAM: ").Append(ramUsedGb.ToString("0.0")).Append(" / ").Append(ramTotalGb.ToString("0.0"))
              .Append(" GB (").Append(Percent(ramUsage)).AppendLine(")");
            sb.Append("GPU: ");
            if (gpuSensor.Name != null) sb.Append(gpuSensor.Name).Append("  ·  ");
            sb.Append(gpuUsage.HasValue ? Percent(gpuUsage.Value) : "--");
            if (gpuSensor.Temperature.HasValue) sb.Append("  ·  ").Append(Temp(gpuSensor.Temperature));
            if (gpuSensor.FanRpm > 0) sb.Append("  ·  ").Append(gpuSensor.FanRpm).Append(" RPM");
            sb.AppendLine();
            sb.Append("FPS: ");
            if (!fps.Available) sb.Append(L.T("needs administrator rights", "yönetici izni gerekli"));
            else if (fpsValue > 0) sb.Append(fpsValue).Append("  ·  ").Append(ProcessName(fpsPid));
            else sb.Append("--");
            if (updater.UpdateAvailable)
                sb.AppendLine().Append("• ").Append(L.T("Update available: ", "Güncelleme var: ")).Append(updater.LatestText)
                  .Append(L.T(" (right-click)", " (sağ tık)"));
            if (settings.ShowNet)
                sb.AppendLine().Append(L.T("Network", "Ağ")).Append(": ↓ ").Append(NetMonitor.Format(net.Down))
                  .Append("   ↑ ").Append(NetMonitor.Format(net.Up));
            return sb.ToString();
        }

        static string ProcessName(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return p.ProcessName + ".exe"; }
            catch { return "?"; }
        }

        // --- drawing ---

        struct DrawOp
        {
            public int X, Y, Width;
            public string Text;
            public uint Color;   // ARGB; alpha dims labels
        }

        void Render(IntPtr tray, bool fullscreen)
        {
            RECT tb;
            if (!GetWindowRect(tray, out tb)) return;
            int rightEdge = tb.Right;
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT nr;
            if (notify != IntPtr.Zero && GetWindowRect(notify, out nr) && nr.Right > nr.Left) rightEdge = nr.Left;

            uint dpi = GetDpiForWindow(tray);
            if (dpi == 0) dpi = 96;
            EnsureFont(dpi);

            bool light = IsLightTaskbar();
            uint normal = light ? 0xFF1A1A1Au : 0xFFF2F2F2u;
            uint label = (light ? 0x00000000u : 0x00F2F2F2u) | 0xA6000000u;   // 65% opacity
            uint warn = light ? 0xFFB35900u : 0xFFFFB74Du;
            uint hot = light ? 0xFFC42B1Cu : 0xFFFF6B6Bu;
            uint accent = light ? 0xFF005FB8u : 0xFF4CC2FFu;

            int height = Math.Max(1, tb.Bottom - tb.Top);
            int pad = Scale(4, dpi);
            int space = TextWidth(" ");
            int cellGap = Scale(12, dpi);
            int lines = settings.TwoLines ? 2 : 1;
            int top = (height - fontHeight * lines) / 2;

            List<Cell> cells = BuildCells();
            var ops = new List<DrawOp>();
            int x = pad;
            for (int i = 0; i < cells.Count; i += lines)
            {
                int columnWidth = 0;
                for (int row = 0; row < lines && i + row < cells.Count; row++)
                {
                    Cell c = cells[i + row];
                    int cx = x, y = top + row * fontHeight;
                    // Text flows naturally after the label; the room the widest values would need is
                    // reserved at the end of the cell, so neighbouring cells do not jitter.
                    int reserved = 0;
                    if (settings.ShowLabels || c.Values.Count == 0 || c.Label.Length == 1)
                    {
                        int w = TextWidth(c.Label);
                        ops.Add(new DrawOp { X = cx, Y = y, Width = w, Text = c.Label, Color = c.Values.Count == 0 ? normal : label });
                        cx += w + space;
                        reserved = w + space;
                    }
                    for (int v = 0; v < c.Values.Count; v++)
                    {
                        if (v > 0) { cx += space; reserved += space; }
                        uint color = !settings.ColorWarnings || c.Levels[v] == Level.Normal ? normal
                                   : c.Levels[v] == Level.Warn ? warn : hot;
                        int w = TextWidth(c.Values[v]);
                        ops.Add(new DrawOp { X = cx, Y = y, Width = w, Text = c.Values[v], Color = color });
                        cx += w;
                        reserved += Math.Max(TextWidth(c.Widest[v]), w);
                    }
                    columnWidth = Math.Max(columnWidth, Math.Max(cx - x, reserved));
                }
                x += columnWidth + cellGap;
            }
            x -= cellGap;
            if (updater.UpdateAvailable)
            {
                // A small accent dot: an update is waiting in the right-click menu.
                string dot = "•";
                x += space;
                ops.Add(new DrawOp { X = x, Y = (height - fontHeight) / 2, Width = TextWidth(dot), Text = dot, Color = accent });
                x += TextWidth(dot);
            }
            int width = x + pad;

            int left = settings.Anchor == 1 ? tb.Left + settings.Offset : rightEdge - settings.Offset - width;
            int windowTop = tb.Top;
            if (fullscreen && embeddedIn == IntPtr.Zero)
            {
                // Explorer can slide the hidden taskbar outside the monitor. Its coordinates
                // are only an anchor hint for an overlay, never a valid clipping boundary.
                var bounds = OverlayLayout.KeepOnScreen(
                    Screen.FromHandle(tray).Bounds,
                    new System.Drawing.Rectangle(left, windowTop, width, height));
                left = bounds.Left;
                windowTop = bounds.Top;
                width = bounds.Width;
                height = bounds.Height;
            }
            Present(ops, width, height);

            if (embeddedIn != IntPtr.Zero)
            {
                // Child of the taskbar: position in its client coordinates, above its XAML content.
                var origin = new POINT();
                ClientToScreen(tray, ref origin);
                SetWindowPos(Handle, HWND_TOP, left - origin.x, tb.Top - origin.y, width, height, SWP_NOACTIVATE);
            }
            else
                SetWindowPos(Handle, HWND_TOPMOST, left, windowTop, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        // Puts the widget inside the taskbar window (so the Start menu's blur and other shell
        // flyouts treat it as part of the taskbar), or back to a topmost overlay when the taskbar
        // is hidden behind a fullscreen app that we were asked to stay visible over.
        void SetEmbedded(IntPtr tray, bool embed)
        {
            if (embed && (embedFailed || embeddedIn == tray)) return;
            if (!embed && embeddedIn == IntPtr.Zero) return;

            int style = GetWindowLong(Handle, GWL_STYLE);
            if (embed)
            {
                SetWindowLong(Handle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
                SetLastError(0);
                if (SetParent(Handle, tray) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    SetWindowLong(Handle, GWL_STYLE, style);   // not allowed here: stay an overlay
                    embedFailed = true;
                    return;
                }
                embeddedIn = tray;
            }
            else
            {
                SetLastError(0);
                if (SetParent(Handle, IntPtr.Zero) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                    return; // keep the real parent state; retry on the next tick
                SetWindowLong(Handle, GWL_STYLE, (style & ~WS_CHILD) | WS_POPUP);
                embeddedIn = IntPtr.Zero;
            }
            // Apply the changed child/popup styles without taking focus from the game.
            SetWindowPos(Handle, embed ? HWND_TOP : HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        void EnsureFont(uint dpi)
        {
            string key = dpi + "|" + settings.FontSize + "|" + settings.Bold;
            if (key == fontKey) return;
            if (font != IntPtr.Zero) DeleteObject(font);

            int px = Scale(settings.FontSize, dpi);
            int weight = settings.Bold ? 600 : 400;
            font = CreateFont(-px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, CLEARTYPE_NATURAL_QUALITY, 0, "Segoe UI Variable Text");
            SelectObject(measureDc, font);
            var face = new StringBuilder(64);
            GetTextFace(measureDc, face.Capacity, face);
            if (face.ToString() != "Segoe UI Variable Text")   // Windows 10
            {
                DeleteObject(font);
                font = CreateFont(-px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, CLEARTYPE_NATURAL_QUALITY, 0, "Segoe UI");
                SelectObject(measureDc, font);
            }
            TEXTMETRIC tm;
            GetTextMetrics(measureDc, out tm);
            fontHeight = tm.tmHeight;
            fontKey = key;
        }

        int TextWidth(string s)
        {
            SIZE size;
            GetTextExtentPoint32(measureDc, s, s.Length, out size);
            return size.cx;
        }

        // Draws white text with GDI onto black, turns the coverage into per-pixel alpha and
        // tints it, then hands the premultiplied bitmap to UpdateLayeredWindow.
        void Present(List<DrawOp> ops, int width, int height)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            var bi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = width,
                biHeight = -height,   // top-down
                biPlanes = 1,
                biBitCount = 32
            };
            IntPtr bits;
            IntPtr dib = CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
            IntPtr oldBitmap = SelectObject(memDc, dib);
            IntPtr oldFont = SelectObject(memDc, font);
            try
            {
                SetBkMode(memDc, TRANSPARENT);
                SetTextColor(memDc, 0xFFFFFF);
                foreach (DrawOp op in ops) TextOut(memDc, op.X, op.Y, op.Text, op.Text.Length);
                GdiFlush();

                int n = width * height;
                var px = new int[n];
                var tint = new uint[n];
                foreach (DrawOp op in ops)
                {
                    int x0 = Math.Max(0, op.X - 1), x1 = Math.Min(width, op.X + op.Width + 2);
                    int y0 = Math.Max(0, op.Y), y1 = Math.Min(height, op.Y + fontHeight);
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++) tint[yy * width + xx] = op.Color;
                }
                Marshal.Copy(bits, px, 0, n);
                for (int i = 0; i < n; i++)
                {
                    int p = px[i];
                    int coverage = (((p >> 16) & 255) + ((p >> 8) & 255) + (p & 255)) / 3;
                    uint c = tint[i];
                    int a = coverage * (int)(c >> 24) / 255;
                    // Alpha 1 everywhere else keeps the whole rectangle clickable while invisible.
                    if (a <= 1) { px[i] = 0x01000000; continue; }
                    int r = (int)((c >> 16) & 255) * a / 255, g = (int)((c >> 8) & 255) * a / 255, b = (int)(c & 255) * a / 255;
                    px[i] = (a << 24) | (r << 16) | (g << 8) | b;
                }
                Marshal.Copy(px, 0, bits, n);

                // Content and size only; Render positions the window (screen or taskbar coordinates).
                var size = new SIZE { cx = width, cy = height };
                var src = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldFont);
                SelectObject(memDc, oldBitmap);
                DeleteObject(dib);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        static bool IsFullscreen(IntPtr fg, IntPtr tray)
        {
            if (fg == IntPtr.Zero || fg == tray || fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "WorkerW" || c == "Progman") return false;

            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            System.Drawing.Rectangle screen = Screen.FromHandle(fg).Bounds;
            if (Screen.FromHandle(tray).Bounds != screen) return false;
            return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        }

        static bool IsLightTaskbar()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k != null ? k.GetValue("SystemUsesLightTheme") : null;
                return v is int && (int)v != 0;
            }
        }

        static int Scale(int px, uint dpi) { return (int)Math.Round(px * dpi / 96.0); }

        // --- menu ---

        void BuildMenu()
        {
            var show = new ToolStripMenuItem(L.T("Show", "Göster"));
            show.DropDownItems.Add(Toggle(L.T("CPU usage", "CPU kullanımı"), () => settings.ShowCpu, v => settings.ShowCpu = v));
            show.DropDownItems.Add(Toggle(L.T("CPU temperature", "CPU sıcaklığı"), () => settings.ShowCpuTemp, v => settings.ShowCpuTemp = v));
            show.DropDownItems.Add(Toggle(L.T("RAM usage", "RAM kullanımı"), () => settings.ShowRam, v => settings.ShowRam = v));
            show.DropDownItems.Add(Toggle(L.T("GPU usage", "GPU kullanımı"), () => settings.ShowGpu, v => settings.ShowGpu = v));
            show.DropDownItems.Add(Toggle(L.T("GPU temperature", "GPU sıcaklığı"), () => settings.ShowGpuTemp, v => settings.ShowGpuTemp = v));
            show.DropDownItems.Add(Toggle("FPS", () => settings.ShowFps, v => settings.ShowFps = v));
            show.DropDownItems.Add(Toggle(L.T("Network speed", "Ağ hızı"), () => settings.ShowNet, v => settings.ShowNet = v));
            KeepOpen(show);

            var look = new ToolStripMenuItem(L.T("Appearance", "Görünüm"));
            look.DropDownItems.Add(Toggle(L.T("Two lines (like the clock)", "İki satır (saat gibi)"), () => settings.TwoLines, v => settings.TwoLines = v));
            look.DropDownItems.Add(Toggle(L.T("Bold text", "Kalın yazı"), () => settings.Bold, v => settings.Bold = v));
            look.DropDownItems.Add(Toggle(L.T("Show labels", "Etiketleri göster"), () => settings.ShowLabels, v => settings.ShowLabels = v));
            look.DropDownItems.Add(Toggle(L.T("Highlight high values", "Yüksek değerleri renklendir"), () => settings.ColorWarnings, v => settings.ColorWarnings = v));
            look.DropDownItems.Add(Toggle(L.T("RAM in GB", "RAM'i GB olarak göster"), () => settings.RamAsGb, v => settings.RamAsGb = v));
            look.DropDownItems.Add(new ToolStripSeparator());
            var size = new ToolStripMenuItem(L.T("Text size", "Yazı boyutu"));
            size.DropDownItems.Add(Choice(L.T("Small", "Küçük"), () => settings.FontSize == 11, () => settings.FontSize = 11));
            size.DropDownItems.Add(Choice(L.T("Normal (clock)", "Normal (saat)"), () => settings.FontSize == 12, () => settings.FontSize = 12));
            size.DropDownItems.Add(Choice(L.T("Large", "Büyük"), () => settings.FontSize == 14, () => settings.FontSize = 14));
            look.DropDownItems.Add(size);
            KeepOpen(look);

            var rate = new ToolStripMenuItem(L.T("Update interval", "Güncelleme sıklığı"));
            rate.DropDownItems.Add(Choice(L.T("0.5 seconds", "0,5 saniye"), () => settings.Interval == 500, () => settings.Interval = 500));
            rate.DropDownItems.Add(Choice(L.T("1 second", "1 saniye"), () => settings.Interval == 1000, () => settings.Interval = 1000));
            rate.DropDownItems.Add(Choice(L.T("2 seconds", "2 saniye"), () => settings.Interval == 2000, () => settings.Interval = 2000));

            var position = new ToolStripMenuItem(L.T("Position", "Konum"));
            position.DropDownItems.Add(Choice(L.T("Next to the clock (right)", "Saatin yanında (sağ)"), () => settings.Anchor == 0, () => { settings.Anchor = 0; settings.Offset = 8; }));
            position.DropDownItems.Add(Choice(L.T("Left edge of the taskbar", "Görev çubuğunun solunda"), () => settings.Anchor == 1, () => { settings.Anchor = 1; settings.Offset = 8; }));
            position.DropDownItems.Add(new ToolStripSeparator());
            position.DropDownItems.Add(new ToolStripMenuItem(L.T("Tip: drag the text to fine-tune", "İpucu: ince ayar için yazıyı sürükle")) { Enabled = false });

            var updateItem = new ToolStripMenuItem("", null, (s, e) => InstallUpdate());
            updateItem.Font = new System.Drawing.Font(updateItem.Font, System.Drawing.FontStyle.Bold);
            refreshChecks.Add(() =>
            {
                updateItem.Visible = updater.UpdateAvailable;
                updateItem.Text = L.T("Install update ", "Güncellemeyi yükle: ") + updater.LatestText;
            });

            var about = new ToolStripMenuItem(L.T("Updates", "Güncellemeler"));
            about.DropDownItems.Add(new ToolStripMenuItem("TaskbarStatus " + Updater.CurrentText) { Enabled = false });
            about.DropDownItems.Add(Toggle(L.T("Check automatically", "Otomatik denetle"), () => settings.CheckUpdates, v => settings.CheckUpdates = v));
            about.DropDownItems.Add(Toggle(L.T("Install automatically in the background", "Arka planda otomatik yükle"), () => settings.AutoInstallUpdates, v => settings.AutoInstallUpdates = v));
            about.DropDownItems.Add(new ToolStripMenuItem(L.T("Check now", "Şimdi denetle"), null, (s, e) => ScheduleUpdateCheck(true)));
            about.DropDownItems.Add(new ToolStripMenuItem(L.T("Open GitHub page", "GitHub sayfasını aç"), null,
                (s, e) => Process.Start("https://github.com/" + Updater.Repo)));

            // Labelled in both languages so it can always be found.
            var language = new ToolStripMenuItem(L.T("Language / Dil", "Dil / Language"));
            language.DropDownItems.Add(Choice(L.T("Windows language", "Windows dili"), () => settings.Language == 0, () => SetLanguage(0)));
            language.DropDownItems.Add(Choice("Türkçe", () => settings.Language == 1, () => SetLanguage(1)));
            language.DropDownItems.Add(Choice("English", () => settings.Language == 2, () => SetLanguage(2)));

            menu.Items.Add(updateItem);
            menu.Items.Add(show);
            menu.Items.Add(look);
            menu.Items.Add(position);
            menu.Items.Add(rate);
            menu.Items.Add(language);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Toggle(L.T("Show over fullscreen apps", "Tam ekranda da göster"), () => settings.ShowInFullscreen, v => settings.ShowInFullscreen = v));
            menu.Items.Add(Toggle(L.T("Start with Windows", "Windows ile başlat"), () => startupEnabled, v =>
            {
                Autostart.Set(v);
                startupEnabled = Autostart.IsEnabled();
            }));
            if (!Autostart.IsAdmin())
                menu.Items.Add(new ToolStripMenuItem(L.T("Restart as administrator (CPU temp, FPS)", "Yönetici olarak başlat (CPU sıcaklığı, FPS)"), null, (s, e) => RestartElevated()));
            if (!PawnIo.Installed)
                menu.Items.Add(new ToolStripMenuItem(L.T("Install PawnIO (for CPU temperature)", "PawnIO'yu kur (CPU sıcaklığı için)"), null, (s, e) => InstallPawnIo()));
            menu.Items.Add(new ToolStripMenuItem(L.T("Open Task Manager", "Görev Yöneticisi'ni aç"), null, (s, e) => OpenTaskManager()));
            menu.Items.Add(about);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L.T("Exit", "Çıkış"), null, (s, e) => Close()));
            Win11Menu.Apply(menu);
        }

        // Rebuilds the menu in the newly chosen language once the current click has finished.
        void SetLanguage(int language)
        {
            settings.Language = language;
            L.Apply(language);
            BeginInvoke((Action)(() =>
            {
                menu.Items.Clear();
                refreshChecks.Clear();
                BuildMenu();
            }));
        }

        ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                set(!get());
                item.Checked = get();
                ApplySettings();
            };
            refreshChecks.Add(() => item.Checked = get());
            return item;
        }

        ToolStripMenuItem Choice(string text, Func<bool> isSelected, Action select)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                select();
                foreach (Action a in refreshChecks) a();
                ApplySettings();
            };
            refreshChecks.Add(() => item.Checked = isSelected());
            return item;
        }

        // Lets several toggles be flipped without the submenu closing after each click.
        static void KeepOpen(ToolStripMenuItem parent)
        {
            parent.DropDown.Closing += (s, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            };
        }

        static void OpenTaskManager()
        {
            try { Process.Start("taskmgr.exe"); }
            catch (System.ComponentModel.Win32Exception) { }
        }

        // --- mouse: drag to move along the taskbar, right-click for the menu, double-click for Task Manager ---

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            mouseDown = true;
            dragging = false;
            dragStartX = Cursor.Position.X;
            dragStartOffset = settings.Offset;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown) return;
            int dx = Cursor.Position.X - dragStartX;
            if (!dragging && Math.Abs(dx) < SystemInformation.DragSize.Width) return;
            dragging = true;
            settings.Offset = Math.Max(0, settings.Anchor == 1 ? dragStartOffset + dx : dragStartOffset - dx);
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero) Render(tray, IsFullscreen(GetForegroundWindow(), tray));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (dragging) settings.Save();
                mouseDown = dragging = false;
                Capture = false;
            }
            else if (e.Button == MouseButtons.Right)
            {
                // The menu needs a foreground top-level window of ours to close when clicking elsewhere;
                // the widget itself may be a child of the taskbar.
                if (menuOwner == null)
                {
                    menuOwner = new Form
                    {
                        FormBorderStyle = FormBorderStyle.FixedToolWindow,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        Location = new System.Drawing.Point(-32000, -32000),
                        Size = new System.Drawing.Size(1, 1)
                    };
                    menuOwner.Show();
                }
                SetForegroundWindow(menuOwner.Handle);
                menu.Show(Cursor.Position);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) OpenTaskManager();
        }

        // --- startup / elevation ---

        void RestartElevated()
        {
            // Prefer the installed task (no UAC prompt); the new instance waits for this one to exit.
            if (Autostart.TaskExists() && Autostart.RunTask())
            {
                Close();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, Program.ElevatedArg)
                {
                    Verb = "runas",
                    UseShellExecute = true
                });
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // UAC prompt was cancelled; keep running as is.
            }
        }

        static void InstallPawnIo()
        {
            try { Process.Start("winget.exe", "install --id namazso.PawnIO -e --accept-package-agreements"); }
            catch (System.ComponentModel.Win32Exception) { Process.Start("https://pawnio.eu"); }
        }

        // --- updates ---

// Checks GitHub off the UI thread. A found update is downloaded right away when automatic
        // installs are on (or the user asked); Tick then runs the installer silently.
        void ScheduleUpdateCheck(bool manual)
        {
            if (Interlocked.CompareExchange(ref updateBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = updater.Check();
                nextUpdateCheck = DateTime.UtcNow.AddHours(ok ? 6 : 1);
                string error = ok ? null : L.T("Could not reach GitHub.", "GitHub'a ulaşılamadı.");
                if (ok && updater.UpdateAvailable && (manual || settings.AutoInstallUpdates))
                    error = DownloadUpdate();
                updateBusy = 0;
                if (!manual || (error == null && updater.UpdateAvailable)) return;   // installing now
                BeginInvoke((Action)(() => MessageBox.Show(
                    error ?? L.T("You are up to date (", "Güncelsin (") + Updater.CurrentText + ").",
                    AppName, MessageBoxButtons.OK, error != null ? MessageBoxIcon.Warning : MessageBoxIcon.Information)));
            });
        }

        // Menu: "Install update" (when automatic installs are off).
        void InstallUpdate()
        {
            if (Interlocked.CompareExchange(ref updateBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = DownloadUpdate();
                updateBusy = 0;
                if (error != null)
                    BeginInvoke((Action)(() => MessageBox.Show(error, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            });
        }

        // Worker thread. Returns an error message, or null once the installer is ready to run.
        string DownloadUpdate()
        {
            try
            {
                pendingInstaller = updater.Download();
                return null;
            }
            catch (Exception ex)
            {
                return L.T("The update could not be downloaded: ", "Güncelleme indirilemedi: ") + ex.Message;
            }
        }

        // UI thread, from Tick: start a downloaded installer unless a fullscreen app (a game) is in front.
        void RunPendingInstaller()
        {
            string installer = pendingInstaller;
            if (installer == null || hidden) return;
            pendingInstaller = null;
            try { Updater.InstallSilently(installer); }
            catch (System.ComponentModel.Win32Exception) { }   // UAC declined (non-elevated copy)
        }

        // --- Win32 ---

        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
        const int ULW_ALPHA = 2, SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, TRANSPARENT = 1;
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10,
                   SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, CLEARTYPE_NATURAL_QUALITY = 6;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_TOP = IntPtr.Zero;
        const int GWL_STYLE = -16, WS_CHILD = 0x40000000;
        const int WS_POPUP = unchecked((int)0x80000000);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TEXTMETRIC
        {
            public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth,
                       tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
            public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
            public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, IntPtr dst, ref SIZE size,
            IntPtr hdcSrc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("kernel32.dll")] static extern void SetLastError(int error);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW")]
        static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextFaceW")] static extern int GetTextFace(IntPtr hdc, int count, StringBuilder face);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextMetricsW")] static extern bool GetTextMetrics(IntPtr hdc, out TEXTMETRIC tm);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")] static extern bool GetTextExtentPoint32(IntPtr hdc, string s, int len, out SIZE size);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "TextOutW")] static extern bool TextOut(IntPtr hdc, int x, int y, string s, int len);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr hdc, uint color);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
    }
}
