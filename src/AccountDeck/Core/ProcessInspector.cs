using System.Runtime.InteropServices;
using System.Text;

namespace AccountDeck.Core;

/// <summary>What we can learn about another process with "query limited information" rights.</summary>
public sealed record ProcessFacts(int Pid, int ParentPid, DateTime StartUtc, string? ImagePath, string? CommandLine);

public static class ProcessInspector
{
    public static ProcessFacts? Read(int pid, bool includeCommandLine)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var start = Native.GetProcessTimes(handle, out var created, out _, out _, out _)
                ? DateTime.FromFileTimeUtc(created)
                : DateTime.MinValue;
            return new ProcessFacts(pid, ParentPid(handle), start, ImagePath(handle),
                includeCommandLine ? CommandLine(handle) : null);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    public static IReadOnlyList<string> SplitArgs(string commandLine)
    {
        var argv = Native.CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero) return Array.Empty<string>();
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return result;
        }
        finally
        {
            Native.LocalFree(argv);
        }
    }

    static string? ImagePath(IntPtr handle)
    {
        var size = 1024;
        var buffer = new StringBuilder(size);
        return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    static int ParentPid(IntPtr handle)
    {
        // PROCESS_BASIC_INFORMATION is six pointer-sized fields; the parent id is the last one.
        var size = IntPtr.Size * 6;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (Native.NtQueryInformationProcess(handle, Native.ProcessBasicInformation, buffer, size, out _) != 0) return 0;
            return (int)Marshal.ReadIntPtr(buffer, IntPtr.Size * 5).ToInt64();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static string? CommandLine(IntPtr handle)
    {
        Native.NtQueryInformationProcess(handle, Native.ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
        if (needed <= 0) return null;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (Native.NtQueryInformationProcess(handle, Native.ProcessCommandLineInformation, buffer, needed, out _) != 0)
                return null;

            // A UNICODE_STRING header: ushort byte length, ushort capacity, then the text pointer.
            var byteLength = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return byteLength == 0 ? string.Empty : Marshal.PtrToStringUni(text, byteLength / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
