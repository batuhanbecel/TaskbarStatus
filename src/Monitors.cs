using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskbarStatus
{
    // CPU usage from GetSystemTimes deltas (kernel time includes idle time).
    class CpuMonitor
    {
        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        long lastIdle, lastKernel, lastUser;

        public CpuMonitor()
        {
            GetSystemTimes(out lastIdle, out lastKernel, out lastUser);
        }

        public float Read()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return 0;
            long dIdle = idle - lastIdle, dTotal = (kernel - lastKernel) + (user - lastUser);
            lastIdle = idle; lastKernel = kernel; lastUser = user;
            if (dTotal <= 0) return 0;
            return Math.Max(0f, Math.Min(100f, 100f * (dTotal - dIdle) / dTotal));
        }
    }

    static class RamMonitor
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static float Read(out double usedGb, out double totalGb)
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(m);
            usedGb = totalGb = 0;
            if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return 0;
            const double GB = 1024.0 * 1024 * 1024;
            usedGb = (m.ullTotalPhys - m.ullAvailPhys) / GB;
            totalGb = m.ullTotalPhys / GB;
            return 100f * (m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys;
        }
    }

    // GPU usage the way Task Manager computes it: sum the per-process "GPU Engine"
    // utilization for each physical engine, then take the busiest engine.
    class GpuMonitor
    {
        PerformanceCounterCategory category;
        Dictionary<string, CounterSample> previous = new Dictionary<string, CounterSample>();
        int failures;

        public float? Read()
        {
            if (failures > 5) return null;
            try
            {
                if (category == null) category = new PerformanceCounterCategory("GPU Engine");
                InstanceDataCollection util = category.ReadCategory()["Utilization Percentage"];
                if (util == null) { failures++; return null; }

                var current = new Dictionary<string, CounterSample>(previous.Count + 16);
                var perEngine = new Dictionary<string, float>();
                foreach (InstanceData data in util.Values)
                {
                    string name = data.InstanceName;
                    current[name] = data.Sample;
                    CounterSample old;
                    if (!previous.TryGetValue(name, out old)) continue;

                    // "pid_1234_luid_0x..._phys_0_eng_3_engtype_3D" -> engine key without the pid.
                    int i = name.IndexOf("_luid_", StringComparison.Ordinal);
                    string key = i >= 0 ? name.Substring(i) : name;
                    float sum;
                    perEngine.TryGetValue(key, out sum);
                    perEngine[key] = sum + CounterSample.Calculate(old, data.Sample);
                }
                previous = current;
                failures = 0;

                float max = 0;
                foreach (float v in perEngine.Values) if (v > max) max = v;
                return Math.Min(max, 100f);
            }
            catch
            {
                failures++;
                return null;
            }
        }
    }

    // FPS of the foreground window's process, counted from DXGI Present events
    // through a real-time ETW session. Needs admin (or the "Performance Log Users" group).
    class FpsMonitor : IDisposable
    {
        const string SessionName = "TaskbarStatusFpsSession";
        static Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        const int DxgiProviderFirstDword = unchecked((int)0xCA11C036);
        const ushort PresentStart = 42, PresentMultiplaneOverlayStart = 55;

        const uint EVENT_TRACE_REAL_TIME_MODE = 0x100;
        const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        const uint PROCESS_TRACE_MODE_REAL_TIME = 0x100;
        const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        const uint EVENT_TRACE_CONTROL_STOP = 1;
        const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        const int ERROR_ALREADY_EXISTS = 183;
        const ulong INVALID_PROCESSTRACE_HANDLE = 0xFFFFFFFFFFFFFFFF;

        // x64 layouts (the exe is built for x64 only).
        const int PropsHeaderSize = 120;           // sizeof(EVENT_TRACE_PROPERTIES)
        const int PropsSize = PropsHeaderSize + 2048;
        const int LogFileSize = 448;               // sizeof(EVENT_TRACE_LOGFILEW)

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void EventRecordCallback(IntPtr eventRecord);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")]
        static extern int StartTrace(out ulong handle, string name, IntPtr properties);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
        static extern int ControlTrace(ulong handle, string name, IntPtr properties, uint code);
        [DllImport("advapi32.dll")]
        static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint controlCode, byte level,
            ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
        [DllImport("advapi32.dll", EntryPoint = "OpenTraceW")]
        static extern ulong OpenTrace(IntPtr logFile);
        [DllImport("advapi32.dll")]
        static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")]
        static extern int CloseTrace(ulong handle);

        readonly EventRecordCallback callback;
        ulong traceHandle = INVALID_PROCESSTRACE_HANDLE;
        IntPtr logFile, loggerName;
        volatile int targetPid;
        int frames;
        readonly Stopwatch clock = Stopwatch.StartNew();

        public bool Available { get; private set; }

        public FpsMonitor()
        {
            callback = OnEvent;
            try { Available = Start(); }
            catch { Available = false; }
        }

        public int TargetPid { set { targetPid = value; } }

        // Frames presented by the target process since the last call, per second.
        public int Read()
        {
            int count = Interlocked.Exchange(ref frames, 0);
            double seconds = clock.Elapsed.TotalSeconds;
            clock.Restart();
            return seconds > 0 ? (int)Math.Round(count / seconds) : 0;
        }

        bool Start()
        {
            ulong session;
            IntPtr props = NewProperties();
            int err = StartTrace(out session, SessionName, props);
            if (err == ERROR_ALREADY_EXISTS)
            {
                // Left over from a previous run that did not exit cleanly.
                StopSession();
                Marshal.FreeHGlobal(props);
                props = NewProperties();
                err = StartTrace(out session, SessionName, props);
            }
            Marshal.FreeHGlobal(props);
            if (err != 0) return false;

            Guid provider = DxgiProvider;
            if (EnableTraceEx2(session, ref provider, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4,
                    ulong.MaxValue, 0, 0, IntPtr.Zero) != 0)
            {
                StopSession();
                return false;
            }

            logFile = Alloc(LogFileSize);
            loggerName = Marshal.StringToHGlobalUni(SessionName);
            Marshal.WriteIntPtr(logFile, 8, loggerName);
            Marshal.WriteInt32(logFile, 28, unchecked((int)(PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD)));
            Marshal.WriteIntPtr(logFile, 424, Marshal.GetFunctionPointerForDelegate(callback));

            traceHandle = OpenTrace(logFile);
            if (traceHandle == INVALID_PROCESSTRACE_HANDLE)
            {
                StopSession();
                return false;
            }

            ulong handle = traceHandle;
            var thread = new Thread(() => ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero));
            thread.IsBackground = true;
            thread.Name = "ETW consumer";
            thread.Start();
            return true;
        }

        void OnEvent(IntPtr record)
        {
            // EVENT_RECORD.EventHeader: ProcessId @12, ProviderId @24, EventDescriptor.Id @40.
            ushort id = (ushort)Marshal.ReadInt16(record, 40);
            if (id != PresentStart && id != PresentMultiplaneOverlayStart) return;
            if (Marshal.ReadInt32(record, 24) != DxgiProviderFirstDword) return;
            if (Marshal.ReadInt32(record, 12) == targetPid) Interlocked.Increment(ref frames);
        }

        static IntPtr NewProperties()
        {
            IntPtr p = Alloc(PropsSize);
            Marshal.WriteInt32(p, 0, PropsSize);                         // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);                                // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, unchecked((int)WNODE_FLAG_TRACED_GUID));
            Marshal.WriteInt32(p, 48, 64);                               // BufferSize (KB)
            Marshal.WriteInt32(p, 64, (int)EVENT_TRACE_REAL_TIME_MODE);  // LogFileMode
            Marshal.WriteInt32(p, 68, 1);                                // FlushTimer (s)
            Marshal.WriteInt32(p, 116, PropsHeaderSize);                 // LoggerNameOffset
            return p;
        }

        static IntPtr Alloc(int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0);
            return p;
        }

        static void StopSession()
        {
            IntPtr props = NewProperties();
            ControlTrace(0, SessionName, props, EVENT_TRACE_CONTROL_STOP);
            Marshal.FreeHGlobal(props);
        }

        public void Dispose()
        {
            if (!Available) return;
            Available = false;
            StopSession();
            if (traceHandle != INVALID_PROCESSTRACE_HANDLE) CloseTrace(traceHandle);
            // logFile/loggerName are intentionally not freed: the consumer thread may still
            // be unwinding, and the process is exiting anyway.
        }
    }
}
