using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;

namespace Crystal.Scripts;

public partial class Main : Node
{
    public override void _Ready()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || 
            RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            GetTree().Quit();
        }
        // 捕获C#托管代码的未处理异常（主线程）
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        // 捕获异步任务的未处理异常
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    // 全局未处理异常回调
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            // 输出到Godot控制台
            GD.PrintErr($"【全局未处理异常】：{ex.Message}\n堆栈跟踪：{ex.StackTrace}");
            // 写入本地日志文件（便于离线排查）
            string logPath = ProjectSettings.GlobalizePath("user://crash_log.txt");
            using var sw = System.IO.File.AppendText(logPath);
            sw.WriteLine($"[{DateTime.Now}] 崩溃原因：{ex.Message}");
            sw.WriteLine($"堆栈跟踪：{ex.StackTrace}");
            sw.WriteLine("----------------------------------------");
        }
    }

    // 异步任务未观察到的异常回调
    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        GD.PrintErr($"【异步任务异常】：{e.Exception.Message}\n堆栈跟踪：{e.Exception.StackTrace}");
        e.SetObserved(); // 标记异常已处理，避免程序崩溃
    }
}