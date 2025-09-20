using System;
using System.Runtime.InteropServices;

namespace YouTubeDownloader;

internal static class NativeWin32
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, MoveFileFlags dwFlags);

    [Flags]
    private enum MoveFileFlags : uint
    {
        MOVEFILE_DELAY_UNTIL_REBOOT = 0x4,
        MOVEFILE_REPLACE_EXISTING = 0x1
    }

    public static void ScheduleDeleteOnReboot(string path)
    {
        try
        {
            MoveFileEx(path, null, MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        catch { }
    }
}

