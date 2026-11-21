using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RiftLens.UI;

internal static class SingleInstance
{
    public const string MutexName = @"Local\RiftLens.SingleInstance";
    public static readonly int WmShowExisting = NativeMethods.RegisterWindowMessage("RiftLens.ShowExisting.v1");

    public static void RequestExistingInstanceToShow()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("RiftLens"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    NativeMethods.AllowSetForegroundWindow(process.Id);
                }
            }
        }
        catch
        {
            // Showing the existing window is best-effort; the mutex still
            // prevents a second tray icon even if activation fails.
        }

        NativeMethods.PostMessage(NativeMethods.HwndBroadcast, WmShowExisting, IntPtr.Zero, IntPtr.Zero);
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HwndBroadcast = (IntPtr)0xffff;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);
    }
}
