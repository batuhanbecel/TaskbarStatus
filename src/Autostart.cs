using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarStatus
{
    // CPU temperature (PawnIO) and FPS (ETW) need administrator rights. A scheduled task with
    // "highest privileges" lets the app start elevated without a UAC prompt: normal launches hop
    // through the task, and the task's logon trigger doubles as "start with Windows".
    static class Autostart
    {
        public const string TaskName = "TaskbarStatus";
        public const string FromTaskArg = "--from-task";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAdmin()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool TaskExists()
        {
            string output;
            return Schtasks("/Query /TN " + TaskName, out output) == 0;
        }

        public static bool RunTask()
        {
            string output;
            return Schtasks("/Run /TN " + TaskName, out output) == 0;
        }

        public static bool IsEnabled()
        {
            string xml;
            if (Schtasks("/Query /TN " + TaskName + " /XML", out xml) == 0 && xml.Contains("<LogonTrigger>")) return true;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(TaskName) != null;
        }

        // Creates (or replaces) the elevated task; requires admin. atLogon adds the logon trigger.
        public static bool Register(bool atLogon)
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>TaskbarStatus (elevated start)</Description></RegistrationInfo>\r\n" +
                "  <Triggers>" + (atLogon ? "<LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger>" : "") + "</Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>6</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>" + FromTaskArg + "</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";

            string file = Path.Combine(Path.GetTempPath(), "TaskbarStatus-task.xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            try
            {
                string output;
                bool ok = Schtasks("/Create /TN " + TaskName + " /XML \"" + file + "\" /F", out output) == 0;
                if (ok) RemoveRunKey();
                return ok;
            }
            finally { File.Delete(file); }
        }

        public static bool Unregister()
        {
            string output;
            return Schtasks("/Delete /TN " + TaskName + " /F", out output) == 0;
        }

        // Turns start-with-Windows on or off, asking for elevation once if needed.
        public static void Set(bool enable)
        {
            if (IsAdmin())
            {
                Register(enable);
                if (!enable) RemoveRunKey();
                return;
            }
            if (RunElevated("--register-task " + (enable ? "on" : "off"))) return;

            // UAC was declined: fall back to a plain (non-elevated) Run entry.
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable) k.SetValue(TaskName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(TaskName, false);
            }
        }

        static void RemoveRunKey()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(TaskName, false);
        }

        public static bool RunElevated(string arguments)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments)
                {
                    Verb = "runas",
                    UseShellExecute = true
                }))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;   // UAC prompt cancelled
            }
        }

        static int Schtasks(string arguments, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
