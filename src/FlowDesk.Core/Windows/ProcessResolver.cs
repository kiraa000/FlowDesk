using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using static FlowDesk.Core.Interop.Native;

namespace FlowDesk.Core.Windows;

/// <summary>Exe path, AUMID and arguments of a process, read with limited-query access.</summary>
public sealed record ProcessDetails(string ExePath, string? Aumid, string? Arguments);

/// <summary>
/// Resolves process details with a short cache (PIDs are reused, so entries expire).
/// Works for elevated processes too, because PROCESS_QUERY_LIMITED_INFORMATION is enough.
/// </summary>
public sealed class ProcessResolver
{
    private readonly ConcurrentDictionary<int, (ProcessDetails D, long Tick)> _cache = new();
    private const long TtlMs = 15_000;

    public ProcessDetails Resolve(int pid)
    {
        long now = Environment.TickCount64;
        if (_cache.TryGetValue(pid, out var hit) && now - hit.Tick < TtlMs) return hit.D;
        var d = Query(pid);
        _cache[pid] = (d, now);
        return d;
    }

    private static ProcessDetails Query(int pid)
    {
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == 0) return new ProcessDetails("", null, null);
        try
        {
            return new ProcessDetails(ExePath(h), Aumid(h), Arguments(h));
        }
        finally { CloseHandle(h); }
    }

    private static string ExePath(nint h)
    {
        var sb = new StringBuilder(1024);
        int size = sb.Capacity;
        return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : "";
    }

    private static string? Aumid(nint h)
    {
        int len = 0;
        int rc = GetApplicationUserModelId(h, ref len, null);
        if (rc != ERROR_INSUFFICIENT_BUFFER || len <= 0) return null; // not packaged (or no access)
        var sb = new StringBuilder(len);
        return GetApplicationUserModelId(h, ref len, sb) == 0 ? sb.ToString() : null;
    }

    /// <summary>Reads the full command line (Windows 8.1+) and strips the exe part.</summary>
    private static string? Arguments(nint h)
    {
        int size = 2048;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            nint buf = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, size, out int needed);
                if (status == unchecked((int)0xC0000004) || status == unchecked((int)0xC0000023)) // length mismatch / buffer too small
                {
                    size = Math.Max(needed, size * 2);
                    continue;
                }
                if (status != 0) return null;
                // UNICODE_STRING { ushort Length; ushort MaximumLength; [pad]; PWSTR Buffer; }
                int byteLen = (ushort)Marshal.ReadInt16(buf);
                nint text = Marshal.ReadIntPtr(buf, IntPtr.Size);
                if (text == 0 || byteLen == 0) return null;
                return StripExe(Marshal.PtrToStringUni(text, byteLen / 2));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return null;
    }

    /// <summary>"\"C:\x\a.exe\" --foo bar" -> "--foo bar"; "a.exe x" -> "x". Empty -> null.</summary>
    public static string? StripExe(string commandLine)
    {
        string s = commandLine.TrimStart();
        int rest;
        if (s.StartsWith('"'))
        {
            int close = s.IndexOf('"', 1);
            rest = close < 0 ? s.Length : close + 1;
        }
        else
        {
            int sp = s.IndexOf(' ');
            rest = sp < 0 ? s.Length : sp;
        }
        string args = s[rest..].Trim();
        return args.Length == 0 ? null : args;
    }
}
