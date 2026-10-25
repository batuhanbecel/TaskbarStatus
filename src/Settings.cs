using System.Globalization;
using System.Reflection;
using Microsoft.Win32;

namespace TaskbarStatus
{
    // User preferences, persisted as DWORDs under HKCU\Software\TaskbarStatus (one value per public field).
    class Settings
    {
        public static string Key = @"Software\TaskbarStatus";

        public bool ShowCpu = true;
        public bool ShowCpuTemp = true;
        public bool ShowRam = true;
        public bool ShowGpu = true;
        public bool ShowGpuTemp = true;
        public bool ShowFps = true;
        public bool ShowNet = false;

        public bool TwoLines = false;
        public bool Bold = false;
        public bool ShowLabels = true;
        public bool ColorWarnings = true;
        public bool RamAsGb = false;
        public bool ShowInFullscreen = false;
        public bool CheckUpdates = true;
        public bool AutoInstallUpdates = true;

        public int Anchor = 0;         // 0 = next to the tray (right), 1 = left edge of the taskbar
        public int Language = 0;       // 0 = Windows language, 1 = Turkish, 2 = English
        public int FontSize = 12;      // px at 100% scaling; 12 matches the taskbar clock
        public int Interval = 1000;    // ms
        public int Offset = 8;         // gap to the anchor edge, physical px

        public static Settings Load()
        {
            var s = new Settings();
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                if (k == null) return s;
                foreach (FieldInfo f in Fields())
                {
                    object v = k.GetValue(f.Name);
                    if (!(v is int)) continue;
                    if (f.FieldType == typeof(bool)) f.SetValue(s, (int)v != 0);
                    else f.SetValue(s, (int)v);
                }
            }
            return s;
        }

        public void Save()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                foreach (FieldInfo f in Fields())
                {
                    object v = f.GetValue(this);
                    int dword = v is bool ? ((bool)v ? 1 : 0) : (int)v;
                    k.SetValue(f.Name, dword, RegistryValueKind.DWord);
                }
            }
        }

        static FieldInfo[] Fields()
        {
            return typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }
    }

    // UI language: 0 = follow Windows (Turkish on Turkish systems, English elsewhere), 1 = Turkish, 2 = English.
    static class L
    {
        static readonly bool SystemTurkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        static bool turkish = SystemTurkish;

        public static void Apply(int language)
        {
            turkish = language == 1 || (language == 0 && SystemTurkish);
        }

        public static string T(string english, string turkishText)
        {
            return turkish ? turkishText : english;
        }
    }
}
