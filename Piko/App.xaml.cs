using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Piko;

public partial class App : Application
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;
    private static System.Threading.Mutex? _singleInstanceMutex;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

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

        var currentPid = Environment.ProcessId;
        var runningPiko = System.Diagnostics.Process.GetProcessesByName("Piko").FirstOrDefault(p => p.Id != currentPid);

        if (!isNewInstance && runningPiko != null)
        {
            LogDebug("App.OnStartup", "Another instance of Piko is already running. Activating existing instance and exiting.");
            try
            {
                AllowSetForegroundWindow(ASFW_ANY);
                AllowSetForegroundWindow(runningPiko.Id);
            }
            catch { }

            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "piko_recover.flag"), "show");
            }
            catch { }

            try
            {
                var msg = RegisterWindowMessage("PIKO_SHOW_DYNAMIC_ISLAND");
                if (msg != 0)
                {
                    PostMessage(new IntPtr(0xffff), msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                LogDebug("App.OnStartup", $"PostMessage failed: {ex.Message}");
            }

            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
                var response = client.GetAsync("http://127.0.0.1:17321/show").GetAwaiter().GetResult();
                LogDebug("App.OnStartup", $"Existing instance /show response status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                LogDebug("App.OnStartup", $"Failed to notify existing instance via HTTP: {ex.Message}");
            }
            Shutdown(0);
            return;
        }

        LogDebug("App.OnStartup", $"Application starting. Args: [{(e.Args != null ? string.Join(", ", e.Args) : "")}]");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        base.OnStartup(e);

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Closing += (s, args) => LogDebug("MainWindow", $"Closing event raised. Cancel={args.Cancel}");
            mainWindow.Closed += (s, args) => LogDebug("MainWindow", "Closed event raised.");
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogException("MainWindowCreationFatal", ex);
            MessageBox.Show($"Piko gagal dimulai:\n{ex.Message}", "Piko Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogDebug("App.OnExit", $"Application exiting with code: {e.ApplicationExitCode}. CallStack:\n{Environment.StackTrace}");
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
        if (MainWindow == null || !MainWindow.IsLoaded)
        {
            // Fatal startup error before window is even ready - do not leave zombie process
            Shutdown(1);
            return;
        }
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
