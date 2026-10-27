using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Piko;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LogDebug("App.OnStartup", $"Application starting. Args: [{(e.Args != null ? string.Join(", ", e.Args) : "")}]");
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("CurrentDomain_UnhandledException", ex);
        }
    }

    private static void LogDebug(string stage, string message)
    {
        try
        {
            var logFile = Path.Combine(Path.GetTempPath(), "piko_debug.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}\n");
        }
        catch { }
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            var debugFile = Path.Combine(Path.GetTempPath(), "piko_debug.log");
            File.AppendAllText(debugFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EXCEPTION:{source}] {ex}\n\n");
            var crashFile = Path.Combine(Path.GetTempPath(), "piko_crash.log");
            File.AppendAllText(crashFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}: {ex}\n\n");
        }
        catch { }
    }
}
