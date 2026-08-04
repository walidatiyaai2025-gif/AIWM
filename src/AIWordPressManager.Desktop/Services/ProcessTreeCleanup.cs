using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AIWordPressManager.Desktop.Services;

public static class ProcessTreeCleanup
{
    [StructLayout(LayoutKind.Sequential)] private struct PROCESSENTRY32 { public uint dwSize; public uint cntUsage; public uint th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID; public uint cntThreads; public uint th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string szExeFile; }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll")] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static void KillDescendantsOfCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = (uint)Environment.ProcessId; var parentMap = Snapshot();
        var ids = new HashSet<uint>(); var frontier = new Queue<uint>(); frontier.Enqueue(root);
        while (frontier.Count > 0) { var parent = frontier.Dequeue(); foreach (var pair in parentMap.Where(x => x.Value == parent)) if (ids.Add(pair.Key)) frontier.Enqueue(pair.Key); }
        foreach (var id in ids.Reverse()) { try { using var p = Process.GetProcessById((int)id); if (!p.HasExited) p.Kill(entireProcessTree:true); } catch { } }
    }
    private static Dictionary<uint,uint> Snapshot()
    {
        var result = new Dictionary<uint,uint>(); var snap = CreateToolhelp32Snapshot(2,0); if (snap == new IntPtr(-1)) return result;
        try { var e = new PROCESSENTRY32 { dwSize=(uint)Marshal.SizeOf<PROCESSENTRY32>() }; if (!Process32First(snap, ref e)) return result; do { result[e.th32ProcessID]=e.th32ParentProcessID; } while (Process32Next(snap, ref e)); } finally { CloseHandle(snap); } return result;
    }
}
