using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarStatus
{
    static class Program
    {
        public const string ElevatedArg = "--elevated";

        // --dev: runs next to the installed copy for testing (own settings, no task hop, no updates).
        public static bool Dev { get; private set; }
        public static bool Diagnose { get; private set; }

        [STAThread]
        static int Main(string[] args)
        {
            // Helper modes used by the installer and by the "Start with Windows" toggle (run elevated).
            int i = Array.IndexOf(args, "--register-task");
            if (i >= 0) return Autostart.Register(i + 1 < args.Length && args[i + 1] == "on") ? 0 : 1;
            if (args.Contains("--unregister-task")) return Autostart.Unregister() ? 0 : 1;

            Dev = args.Contains("--dev");
            Diagnose = args.Contains("--diagnose");
            if (Dev) Settings.Key = @"Software\TaskbarStatus.Dev";

            // Not elevated but the elevated task is installed: start through it (no UAC prompt).
            bool fromTask = args.Contains(Autostart.FromTaskArg);
            if (!Dev && !Diagnose && !fromTask && !args.Contains(ElevatedArg) && !Autostart.IsAdmin() &&
                Autostart.TaskExists() && Autostart.RunTask())
                return 0;

            bool owned;
            using (var mutex = new Mutex(true, Dev ? "TaskbarStatus_Dev" : "TaskbarStatus_SingleInstance", out owned))
            {
                // When restarting elevated, give the previous instance a moment to exit.
                if (!owned && (fromTask || args.Contains(ElevatedArg)))
                {
                    try { owned = mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { owned = true; }
                }
                if (!owned) return 0;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new StatusWindow());

                // Explorer restarted and took the embedded widget with it: start again (keeps elevation).
                if (StatusWindow.RestartRequested)
                    Process.Start(Application.ExecutablePath, Dev ? "--dev" : ElevatedArg);
                mutex.ReleaseMutex();
            }
            return 0;
        }
    }
}
