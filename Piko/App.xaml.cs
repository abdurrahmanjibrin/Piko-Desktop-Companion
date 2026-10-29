using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Piko;

public partial class App : Application
{
    private static System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Local\\PikoDesktopCompanion_SingleInstance_Mutex";
        bool isNewInstance = false;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out isNewInstance);
        }
        catch (System.Threading.AbandonedMutexException)
        {
            isNewInstance = true;
        }
        catch (Exception ex)
        {
            LogException("MutexCreation", ex);
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            LogDebug("App.OnStartup", "Another instance of Piko is already running. Activating existing instance and exiting.");
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                client.PostAsync("http://127.0.0.1:17321/show", null).GetAwaiter().GetResult();
            }
            catch { }
            Shutdown();
            return;
        }

        LogDebug("App.OnStartup", $"Application starting. Args: [{(e.Args != null ? string.Join(", ", e.Args) : "")}]");
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
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
