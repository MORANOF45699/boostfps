using System.Runtime.InteropServices;

namespace BoostFPS.Core.Services;

public sealed record CleanerStep(string Name, bool Success, string Detail);

public sealed record MemoryStats(
    ulong TotalBytes, ulong AvailableBytes, ulong FreeBytes,
    ulong StandbyBytes, ulong ModifiedBytes, ulong CommittedBytes)
{
    public double UsedPercent =>
        TotalBytes == 0 ? 0 : (1.0 - (double)AvailableBytes / TotalBytes) * 100.0;
}

/// <summary>
/// Purges the standby list, working sets, and modified pages via NtSetSystemInformation, then
/// clears temp folders. Requires SE_PROF_SINGLE_PROCESS_NAME / SE_INC_BASE_PRIORITY_NAME which
/// admin already has (we run elevated), so no extra privilege escalation needed here.
/// </summary>
public sealed partial class MemoryCleanerService
{
    // NtSetSystemInformation class constants for the memory-list commands.
    private const int SystemMemoryListInformation = 80;

    private enum MemoryListCommand
    {
        MemoryCaptureAccessedBits = 0,
        MemoryCaptureAndResetAccessedBits = 1,
        MemoryEmptyWorkingSets = 2,
        MemoryFlushModifiedList = 3,
        MemoryPurgeStandbyList = 4,
        MemoryPurgeLowPriorityStandbyList = 5
    }

    [LibraryImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    private static partial int NtSetSystemInformation(int cls, ref int info, int length);

    [LibraryImport("psapi.dll", EntryPoint = "EmptyWorkingSet", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public MemoryStats ReadStats()
    {
        var s = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref s);

        // Standby / modified are only exposed via performance counters or SystemMemoryInformation
        // (undocumented). Reading them precisely is optional for the UI so we omit here (0).
        return new MemoryStats(
            s.ullTotalPhys, s.ullAvailPhys, s.ullAvailPhys, 0, 0,
            s.ullTotalPageFile - s.ullAvailPageFile);
    }

    public CleanerStep PurgeStandbyList() =>
        Invoke("Purge standby list", MemoryListCommand.MemoryPurgeStandbyList);

    public CleanerStep PurgeLowPriorityStandbyList() =>
        Invoke("Purge low-priority standby", MemoryListCommand.MemoryPurgeLowPriorityStandbyList);

    public CleanerStep FlushModifiedPageList() =>
        Invoke("Flush modified page list", MemoryListCommand.MemoryFlushModifiedList);

    public CleanerStep EmptyAllWorkingSets() =>
        Invoke("Empty working sets", MemoryListCommand.MemoryEmptyWorkingSets);

    /// <summary>Best-effort empty of every accessible process's working set. Silent on failures.</summary>
    public CleanerStep EmptyWorkingSetPerProcess()
    {
        var count = 0;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (EmptyWorkingSet(p.Handle)) count++;
            }
            catch { /* not accessible */ }
            finally { p.Dispose(); }
        }
        return new CleanerStep("Empty per-process working sets", true, $"เคลียร์ {count} process");
    }

    public CleanerStep ClearTempFolders()
    {
        var targets = new[]
        {
            Path.GetTempPath(),
            Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Temp"),
            Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Prefetch")
        };

        long freed = 0;
        var errors = 0;

        foreach (var dir in targets.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                try
                {
                    if (File.Exists(entry))
                    {
                        freed += new FileInfo(entry).Length;
                        File.Delete(entry);
                    }
                    else
                    {
                        var info = new DirectoryInfo(entry);
                        freed += info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                        info.Delete(recursive: true);
                    }
                }
                catch { errors++; }
            }
        }

        return new CleanerStep(
            "ล้าง temp",
            errors == 0,
            $"คืน {freed / 1024.0 / 1024:F1} MB" + (errors > 0 ? $" (ข้ามไป {errors} ไฟล์ที่ถูก lock)" : ""));
    }

    private static CleanerStep Invoke(string name, MemoryListCommand command)
    {
        var value = (int)command;
        var rc = NtSetSystemInformation(SystemMemoryListInformation, ref value, sizeof(int));

        // 0 = STATUS_SUCCESS. NT_SUCCESS is any non-negative code, so treat those as OK.
        return new CleanerStep(name, rc >= 0, rc >= 0 ? "OK" : $"NTSTATUS 0x{rc:X8}");
    }
}
