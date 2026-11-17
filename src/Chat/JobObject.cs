using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Windows job object with KILL_ON_JOB_CLOSE: child processes assigned to it die when the game process ends,
/// even if the game is killed (no ProcessExit handler runs then).
/// </summary>
internal sealed class JobObject : IDisposable
{
    private IntPtr _handle;

    private JobObject(IntPtr handle) => _handle = handle;

    public static JobObject? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            IntPtr handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) return null;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
            };
            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ptr, (uint)size))
                {
                    CloseHandle(handle);
                    return null;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return new JobObject(handle);
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: job object unavailable: {ex.Message}");
            return null;
        }
    }

    public void Assign(Process process)
    {
        if (_handle == IntPtr.Zero) return;
        try
        {
            if (!AssignProcessToJobObject(_handle, process.Handle))
                Log.Warn($"chat: could not assign claude to job object ({Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex) { Log.Warn($"chat: job assign failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
