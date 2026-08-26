using System.Diagnostics;
using RiftLens.UI;

namespace RiftLens;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Mutex? mutex = null;
        var createdNew = false;
        try
        {
            mutex = new Mutex(initiallyOwned: true, SingleInstance.MutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            SingleInstance.RequestExistingInstanceToShow();
            mutex?.Dispose();
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // 游戏中不能用模态弹窗打断操作；保留给调试器/系统诊断查看。
            Application.ThreadException += (_, args) =>
                Trace.WriteLine($"[RiftLens] 未处理界面异常：{args.Exception}");
            Application.Run(new MainForm());
        }
        finally
        {
            try
            {
                mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            mutex?.Dispose();
        }
    }
}
