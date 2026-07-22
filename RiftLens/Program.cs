using System.Diagnostics;
using RiftLens.UI;

namespace RiftLens;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // 游戏中不能用模态弹窗打断操作；保留给调试器/系统诊断查看。
        Application.ThreadException += (_, args) =>
            Trace.WriteLine($"[RiftLens] 未处理界面异常：{args.Exception}");
        Application.Run(new MainForm());
    }
}
