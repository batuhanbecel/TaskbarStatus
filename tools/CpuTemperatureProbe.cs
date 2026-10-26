using System;
using System.IO;
using System.Threading;
using TaskbarStatus;

static class CpuTemperatureProbe
{
    static void Main()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cpu-temperature-probe.txt");
        using (var log = new StreamWriter(path, false))
        {
            log.AutoFlush = true;
            log.WriteLine("Administrator: " + Autostart.IsAdmin());
            log.WriteLine("PawnIO installed: " + PawnIo.Installed);
            try
            {
                var monitor = new CpuTempMonitor();
                for (int i = 0; i < 10; i++)
                {
                    float? value = monitor.Read();
                    log.WriteLine(DateTime.Now.ToString("O") + " temperature=" + value + " source=" + monitor.Source);
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex) { log.WriteLine(ex); }
        }
    }
}
