using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Documents;
using Piko.Models;
using Piko.Services;

namespace Piko;

public enum NotchPanelState
{
    Collapsed,
    Expanding,
    Expanded,
    Collapsing
}

public enum ScreenDockEdge
{
    Top,
    Left,
    Right
}

public enum DynamicIslandState
{
    Idle,
    Hover,
    Sleepy,
    PomodoroActive,
    MusicActive,
    MediaPaused,
    AiWorking,
    Notification,
    Success,
    FileEating,
    WaterReminder,
    Expanded,
    Pinned
}

public enum NotificationPriority
{
    Routine = 0,
    Timer = 1,
    Ai = 2
}

public enum PomodoroPhase
{
    Work,
    ShortBreak,
    LongBreak
}

public sealed class IslandNotificationItem
{
    public NotificationPriority Priority { get; init; } = NotificationPriority.Routine;
    public string Icon { get; init; } = "🔔";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
}

public partial class MainWindow : Window
{
    private const double IdleWidth = 96;
    private const double IdleHeight = 36;
    private const double HoverWidth = 140;
    private const double HoverHeight = 38;
    private const double PomodoroActiveWidth = 160;
    private const double PomodoroActiveHeight = 36;
    private const double MusicActiveWidth = 250;
    private const double MusicActiveHeight = 50;
    private const double MusicHoverWidth = 280;
    private const double SleepyWidth = 96;
    private const double SleepyHeight = 36;
    private const double AiWorkingWidth = 335;
    private const double AiWorkingHeight = 44;
    private const double SuccessWidth = 330;
    private const double SuccessHeight = 44;
    private const double NotificationWidth = 320;
    private const double NotificationHeight = 44;
    private const double FileEatingWidth = 360;
    private const double FileEatingHeight = 44;
    private const double ExpandedWidth = 720;
    private const double ExpandedHeight = 510;

    // Services
    private readonly PikoStateManager _stateManager = new();
    private readonly SoundService _soundService = new();
    private readonly LocalBridgeService _bridgeService = new();
    private readonly AntigravityWatcherService _antigravityWatcher = new();
    private readonly StatePersistenceService _persistenceService = new();
    private readonly StorageScannerService _storageScanner = new();
    private readonly StorageOrganizerService _storageOrganizer = new();
    private readonly GoogleCalendarService _calendarService = new();
    private DateTime _currentCalendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // Multi-AI Concurrent Thinking State
    private readonly Dictionary<string, (string Message, DateTime StartTime)> _activeAiSessions = new(StringComparer.OrdinalIgnoreCase);

    // Timers
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _hoverOpenTimer;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _focusTimer;
    private readonly DispatcherTimer _workingAnimationTimer;
    private readonly DispatcherTimer _diagnosticCheckTimer;
    private readonly DispatcherTimer _activityAutoCollapseTimer;
    private readonly DispatcherTimer _equalizerTimer;
    private readonly DispatcherTimer _sleepyTimer;
    private readonly DispatcherTimer _waterReminderTimer;
    private readonly DispatcherTimer _waterChimeRepeatTimer;
    private int _waterSecondsRemaining = 45 * 60;
    private bool _isWaterAlertActive = false;

    // Dynamic Island State Machine & Storyboards
    private DynamicIslandState _currentState = DynamicIslandState.Idle;
    private Storyboard? _zzzStoryboard;
    private readonly Queue<IslandNotificationItem> _notificationQueue = new();
    private int _eqTick;
    private DateTime _lastAiCompleteTime = DateTime.MinValue;

    private AppState _state = new();
    private NotchPanelState _panelState = NotchPanelState.Collapsed;
    private bool _isPinned;
    private bool _isExpanded => _currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned;
    private bool _focusRunning;
    private int _focusSecondsRemaining = 25 * 60;
    private PomodoroPhase _pomodoroPhase = PomodoroPhase.Work;
    private int _pomodoroCycle = 1; // 1 to 4
    private StorageScanResult? _lastScanResult;
    private List<FileOrganizePlanItem> _currentPlan = new();
    private CancellationTokenSource? _scanCts;
    private int _workingAnimStep = 0;

    // Diagnostics & Visibility Fallback
    private bool _visualDiagnosticModeActive;
    private Brush? _originalShellBackground;
    private Brush? _originalShellBorderBrush;
    private Thickness _originalShellBorderThickness;

    // Music demo state & Shared Media Model
    private readonly SharedMediaState _sharedMedia = new();
    private readonly (string Title, string Artist)[] _demoTracks =
    {
        ("Midnight City", "M83 · demo track"),
        ("A Walk", "Tycho · demo track"),
        ("Sunset Lover", "Petit Biscuit · demo track")
    };
    private int _trackIndex;
    private bool _demoPlaying;
    private bool _isMediaPlaying;
    private string _mediaSource = "YouTube";
    private string _mediaTitle = "";
    private PikoEyeController? _eyeController;
    private readonly DispatcherTimer _mediaPauseTimer;
    private MediaControlCommand? _activeCommand;
    private readonly DispatcherTimer _commandTimeoutTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    // Screen Edge Docking & Dragging
    private ScreenDockEdge _dockEdge = ScreenDockEdge.Top;
    private double _customLeft;
    private double _customTop;
    private bool _isCustomPosition;
    private bool _isDoubleDown;
    private bool _isEdgeDragging;
    private bool _hasMovedBeyondThreshold;
    private Point _dragStartCursor;
    private double _dragStartLeft;
    private double _dragStartTop;
    private readonly DispatcherTimer _singleClickTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };

    // Win32 Interop for reliable Topmost Z-order
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public void ShowAndExpandToForeground()
    {
        try
        {
            _activityAutoCollapseTimer.Stop();
            _collapseTimer.Stop();
            _hoverOpenTimer.Stop();

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Visibility = Visibility.Visible;
            Opacity = 1.0;
            Topmost = false;
            Topmost = true;

            // Ensure docked top center
            _dockEdge = ScreenDockEdge.Top;
            _isCustomPosition = false;

            // Expand Piko so the Dynamic Island dashboard is visibly opened on screen
            SetExpanded(true, isUserPin: true);
            PositionNotch(forceTopmost: true);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, 9 /* SW_RESTORE */);
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                // Bypass Windows foreground restrictions using thread attachment
                var foreHwnd = GetForegroundWindow();
                if (foreHwnd != IntPtr.Zero && foreHwnd != hwnd)
                {
                    uint foreThread = GetWindowThreadProcessId(foreHwnd, IntPtr.Zero);
                    uint appThread = GetCurrentThreadId();
                    if (foreThread != 0 && appThread != 0 && foreThread != appThread)
                    {
                        AttachThreadInput(foreThread, appThread, true);
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                        AttachThreadInput(foreThread, appThread, false);
                    }
                    else
                    {
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                    }
                }
                else
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
            }

            Activate();
            BringToTopmostSafe();

            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(2), "Piko aktif ✨");
            _soundService.PlaySuccessChime();
            LogDiagnostic("ShowAndExpandToForeground executed: Piko expanded, brought to foreground & topmost");
        }
        catch (Exception ex)
        {
            LogDiagnostic("ShowAndExpandToForeground-Error", ex);
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        _originalShellBackground = Shell.Background;
        _originalShellBorderBrush = Shell.BorderBrush;
        _originalShellBorderThickness = Shell.BorderThickness;

        // Initialize Central Eye & Headset Controller
        _eyeController = new PikoEyeController(
            EyePair, LeftEyeLine, RightEyeLine,
            LeftEyeScale, RightEyeScale, LeftEyeRotate, RightEyeRotate,
            EyePairTranslate, EyeGlowEffect,
            HeadsetVisual, HeadsetArc, LeftEarcup, RightEarcup, HeadsetScale, HeadsetTranslate);

        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            if (_currentState == DynamicIslandState.WaterReminder)
            {
                DismissWaterReminderAndDrink();
                return;
            }
            ToggleExpand();
        };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _hoverOpenTimer.Tick += HoverOpenTimer_Tick;

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _collapseTimer.Tick += CollapseTimer_Tick;

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 6500)) };
        _blinkTimer.Tick += BlinkTimer_Tick;
        _blinkTimer.Start();

        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusTimer.Tick += FocusTimer_Tick;

        _workingAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _workingAnimationTimer.Tick += WorkingAnimation_Tick;

        // Dynamic Island Timers
        _activityAutoCollapseTimer = new DispatcherTimer();
        _activityAutoCollapseTimer.Tick += ActivityAutoCollapseTimer_Tick;

        _equalizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _equalizerTimer.Tick += EqualizerTimer_Tick;

        _sleepyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(75) };
        _sleepyTimer.Tick += SleepyTimer_Tick;
        _sleepyTimer.Start();

        // Water reminder timers
        _waterReminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _waterReminderTimer.Tick += WaterReminderTimer_Tick;

        _waterChimeRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _waterChimeRepeatTimer.Tick += (_, _) =>
        {
            if (_isWaterAlertActive) _soundService.PlayAlertSound();
        };

        _mediaPauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _mediaPauseTimer.Tick += (_, _) =>
        {
            _mediaPauseTimer.Stop();
            if (_currentState == DynamicIslandState.MediaPaused && !_isMediaPlaying && !_demoPlaying)
            {
                TransitionToState(DynamicIslandState.Idle);
            }
        };

        // Periodic diagnostic check for recovery file flag (%TEMP%\piko_recover.flag) and bounds validity
        _diagnosticCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _diagnosticCheckTimer.Tick += DiagnosticCheckTimer_Tick;
        _diagnosticCheckTimer.Start();

        // Subscribe to State Manager events
        _stateManager.EmotionChanged += StateManager_EmotionChanged;
        _stateManager.StatusChanged += StateManager_StatusChanged;
        _stateManager.NotificationChanged += StateManager_NotificationChanged;

        _commandTimeoutTimer.Tick += CommandTimeoutTimer_Tick;

        // Subscribe to Antigravity watcher events (auto detects Antigravity IDE generation)
        _antigravityWatcher.AntigravityStarted += (source, msg) => Dispatcher.Invoke(() => StartAiWorking(source, msg));
        _antigravityWatcher.AntigravityCompleted += (source, msg) => Dispatcher.Invoke(() => CompleteAiSuccess(source, msg));
        _antigravityWatcher.DiagnosticLogged += msg => LogDiagnostic(msg);

        // Subscribe to Local Bridge events
        _bridgeService.MessageReceived += BridgeService_MessageReceived;
        _bridgeService.StatusChanged += BridgeService_StatusChanged;
        _bridgeService.DiagnosticLogged += msg => LogDiagnostic(msg);
        _bridgeService.DiagnosticRecoverRequested += () => Dispatcher.Invoke(() => TriggerDiagnosticRecovery(true));
        _bridgeService.ToggleVisualRequested += () => Dispatcher.Invoke(ToggleVisualDiagnosticsMode);
        _bridgeService.ExpandRequested += () => Dispatcher.Invoke(ShowAndExpandToForeground);
        _bridgeService.CollapseRequested += () => Dispatcher.Invoke(() => SetExpanded(false, isUserPin: false));
        _bridgeService.SimulateMusicRequested += () => Dispatcher.Invoke(() =>
        {
            _demoPlaying = true;
            PlayPauseButton.Content = "⏸ Pause Demo";
            HomePlayPauseBtn.Content = "⏸ Pause Demo";
            ShowMusicActivity();
        });
        _bridgeService.MediaPlayingWithPayloadReceived += (source, title, payload) => Dispatcher.Invoke(() => HandleMediaPlayingWithPayload(source, title, payload));
        _bridgeService.MediaMetadataReceived += (payload) => Dispatcher.Invoke(() => HandleMediaMetadata(payload));
        _bridgeService.MediaPausedWithPayloadReceived += (source, payload) => Dispatcher.Invoke(() => HandleMediaPausedWithPayload(source, payload));
        _bridgeService.MediaStoppedWithPayloadReceived += (source, payload) => Dispatcher.Invoke(() => HandleMediaStoppedWithPayload(source, payload));
        _bridgeService.MediaCommandResultReceived += result => Dispatcher.Invoke(() => HandleMediaCommandResult(result));
        _bridgeService.SimulateNotificationRequested += () => Dispatcher.Invoke(() =>
        {
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Routine,
                Icon = "💧",
                Title = "Waktunya Minum Air",
                Message = "Jaga hidrasi agar tetap segar dan fokus ✨",
                Duration = TimeSpan.FromSeconds(5)
            });
        });
        _bridgeService.SimulateSleepyRequested += () => Dispatcher.Invoke(() => TransitionToState(DynamicIslandState.Sleepy));
        _bridgeService.SimulateWaterReminderRequested += () => Dispatcher.Invoke(TriggerWaterReminderAlert);
        _bridgeService.SimulateWaterDrinkRequested += () => Dispatcher.Invoke(DismissWaterReminderAndDrink);

        UpdateClock();
        UpdateFocusDisplay();
        UpdateTrackDisplay();
        SyncSharedMediaUI();
        UpdateExpandButtonState();

        DpiChanged += (_, _) => PositionNotch(forceTopmost: true);

        LogDiagnostic("MainWindow.Ctor");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LogDiagnostic("Window_Loaded-Start");
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            LoadAppState();
            _dockEdge = ScreenDockEdge.Top;
            _isCustomPosition = false;
            PositionNotch(forceTopmost: true);
            _bridgeService.Start();
            _antigravityWatcher.Start();

            if (_state.WaterReminderIntervalMinutes <= 0) _state.WaterReminderIntervalMinutes = 45;
            _waterSecondsRemaining = _state.WaterReminderIntervalMinutes * 60;
            _waterReminderTimer.Start();
            InitWaterReminderUI();
            UpdateWaterUI();
            UpdateTodoList();
            UpdateShelfUI();
            UpdateDriveStatsUI();
            RenderCustomTargetFolders();
            RenderRecentEvents();
            ApplyEmotionVisuals(PikoEmotion.Idle);
            InitCalendarUI();
            InitPomodoroUI();
            ApplyTheme();
            SetupNativeListeners();

            // Enforce input foreground colors on focus/text changes
            if (TodoInput != null)
            {
                TodoInput.GotFocus += (_, _) =>
                {
                    var fg = _state.DarkMode ? Brushes.White : Brushes.Black;
                    TodoInput.Foreground = fg;
                    TodoInput.CaretBrush = fg;
                    TextElement.SetForeground(TodoInput, fg);
                };
                TodoInput.TextChanged += (_, _) =>
                {
                    var fg = _state.DarkMode ? Brushes.White : Brushes.Black;
                    TodoInput.Foreground = fg;
                    TodoInput.CaretBrush = fg;
                    TextElement.SetForeground(TodoInput, fg);
                };
            }
            if (TargetFolderInput != null)
            {
                TargetFolderInput.GotFocus += (_, _) =>
                {
                    var fg = _state.DarkMode ? Brushes.White : Brushes.Black;
                    TargetFolderInput.Foreground = fg;
                    TargetFolderInput.CaretBrush = fg;
                    TextElement.SetForeground(TargetFolderInput, fg);
                };
                TargetFolderInput.TextChanged += (_, _) =>
                {
                    var fg = _state.DarkMode ? Brushes.White : Brushes.Black;
                    TargetFolderInput.Foreground = fg;
                    TargetFolderInput.CaretBrush = fg;
                    TextElement.SetForeground(TargetFolderInput, fg);
                };
            }

            // Re-assert positioning, auto-expand, and topmost on Dispatcher loaded priority
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                PositionNotch(forceTopmost: true);
                SetExpanded(true, isUserPin: true);
                Activate();
                BringToTopmostSafe();
                LogDiagnostic("Window_Loaded-PostDispatcherLoaded (AutoExpanded on start)");
            });

            LogDiagnostic($"Window_Loaded-Complete (BridgeRunning={_bridgeService.IsRunning})");
        }
        catch (Exception ex)
        {
            LogDiagnostic("Window_Loaded-Error", ex);
        }
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        LogDiagnostic("Window_ContentRendered");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PositionNotch(forceTopmost: true);
            LogDiagnostic("ContentRendered-PostLayout");
        });
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        LogDiagnostic("Window_Activated");
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        LogDiagnostic($"Window_Deactivated (state={_currentState}, pinned={_isPinned})");
        if (_currentState == DynamicIslandState.Expanded && !_isPinned)
        {
            TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        LogDiagnostic($"Window_StateChanged (WindowState={WindowState})");
        if (WindowState == WindowState.Minimized)
        {
            // Do not leave Piko minimized without a taskbar item
            WindowState = WindowState.Normal;
            PositionNotch(forceTopmost: true);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
            {
                _isPinned = false;
                TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
                e.Handled = true;
                return;
            }
            else if (_currentState != DynamicIslandState.Idle)
            {
                TransitionToState(DynamicIslandState.Idle);
                e.Handled = true;
                return;
            }
        }

        // Fallback hotkeys: F12 or Ctrl+Shift+P to recover position & toggle visual mode
        if (e.Key == Key.F12 || ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && e.Key == Key.P))
        {
            TriggerDiagnosticRecovery(toggleVisual: true);
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        LogDiagnostic("Window_Closing");
        _clockTimer.Stop();
        _collapseTimer.Stop();
        _hoverOpenTimer.Stop();
        _blinkTimer.Stop();
        _focusTimer.Stop();
        _workingAnimationTimer.Stop();
        _diagnosticCheckTimer.Stop();
        _activityAutoCollapseTimer.Stop();
        _equalizerTimer.Stop();
        _sleepyTimer.Stop();
        _scanCts?.Cancel();
        _bridgeService.Dispose();
        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        }
        catch { }
        try
        {
            if (_windowHandle != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_windowHandle);
            }
        }
        catch { }
        _screenshotWatcher?.Dispose();
        SaveAppState();
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => PositionNotch(forceTopmost: true));
    }

    private void DiagnosticCheckTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var flagPath = Path.Combine(Path.GetTempPath(), "piko_recover.flag");
            if (File.Exists(flagPath))
            {
                try { File.Delete(flagPath); } catch { }
                LogDiagnostic("DiagnosticCheckTimer: piko_recover.flag detected, triggering ShowAndExpandToForeground");
                ShowAndExpandToForeground();
                return;
            }

            // Safety check: if window is off-screen or coordinates are NaN/invalid
            if (double.IsNaN(Left) || double.IsNaN(Top) || Left < -15000 || Top < -15000 || WindowState == WindowState.Minimized)
            {
                LogDiagnostic("DiagnosticCheckTimer: Abnormal coordinates or state detected, auto-recovering");
                PositionNotch(forceTopmost: true);
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("DiagnosticCheckTimer-Error", ex);
        }
    }

    public void BringToTopmostSafe()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var source = PresentationSource.FromVisual(this);
                double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                var curW = ActualWidth > 0 ? ActualWidth : Width;
                var curH = ActualHeight > 0 ? ActualHeight : Height;
                var curL = !double.IsNaN(Left) ? Left : 0.0;
                var curT = !double.IsNaN(Top) ? Top : 0.0;

                int physX = (int)Math.Round(curL * dpiScaleX);
                int physY = (int)Math.Round(curT * dpiScaleY);
                int physW = (int)Math.Round(curW * dpiScaleX);
                int physH = (int)Math.Round(curH * dpiScaleY);

                SetWindowPos(handle, HWND_TOPMOST, physX, physY, physW, physH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch
        {
            // Interop fallback
        }
    }

    private void UpdateShellDockBorders()
    {
        double radius = _currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused or DynamicIslandState.Expanded or DynamicIslandState.Pinned ? 22 : 18;

        switch (_dockEdge)
        {
            case ScreenDockEdge.Left:
                Shell.BorderThickness = new Thickness(0, 1, 1, 1);
                Shell.CornerRadius = new CornerRadius(0, radius, radius, 0);
                break;

            case ScreenDockEdge.Right:
                Shell.BorderThickness = new Thickness(1, 1, 0, 1);
                Shell.CornerRadius = new CornerRadius(radius, 0, 0, radius);
                break;

            case ScreenDockEdge.Top:
            default:
                Shell.BorderThickness = new Thickness(1, 0, 1, 1);
                Shell.CornerRadius = new CornerRadius(0, 0, radius, radius);
                break;
        }
    }

    public void PositionNotch(bool forceTopmost = true)
    {
        try
        {
            var primaryWidth = SystemParameters.PrimaryScreenWidth;
            var primaryHeight = SystemParameters.PrimaryScreenHeight;
            var curWidth = _currentState switch
            {
                DynamicIslandState.Idle or DynamicIslandState.Sleepy => IdleWidth,
                DynamicIslandState.Hover => HoverWidth,
                DynamicIslandState.PomodoroActive => PomodoroActiveWidth,
                DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused => MusicActiveWidth,
                DynamicIslandState.AiWorking => AiWorkingWidth,
                DynamicIslandState.Success => SuccessWidth,
                DynamicIslandState.Notification => NotificationWidth,
                DynamicIslandState.FileEating => FileEatingWidth,
                DynamicIslandState.Expanded or DynamicIslandState.Pinned => ExpandedWidth,
                _ => IdleWidth
            };

            var curHeight = _currentState switch
            {
                DynamicIslandState.Expanded or DynamicIslandState.Pinned => ExpandedHeight,
                DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused => MusicActiveHeight,
                DynamicIslandState.PomodoroActive => PomodoroActiveHeight,
                DynamicIslandState.AiWorking => AiWorkingHeight,
                DynamicIslandState.Success => SuccessHeight,
                DynamicIslandState.Notification => NotificationHeight,
                DynamicIslandState.FileEating => FileEatingHeight,
                DynamicIslandState.Hover => HoverHeight,
                DynamicIslandState.Idle or DynamicIslandState.Sleepy => IdleHeight,
                _ => IdleHeight
            };

            double targetLeft;
            double targetTop;

            if (!_isCustomPosition)
            {
                targetLeft = Math.Max(0, (primaryWidth - curWidth) / 2.0);
                targetTop = 0.0;
            }
            else
            {
                switch (_dockEdge)
                {
                    case ScreenDockEdge.Left:
                        targetLeft = 0.0;
                        targetTop = Math.Clamp(_customTop, 0, Math.Max(0, primaryHeight - curHeight));
                        break;
                    case ScreenDockEdge.Right:
                        targetLeft = Math.Max(0, primaryWidth - curWidth);
                        targetTop = Math.Clamp(_customTop, 0, Math.Max(0, primaryHeight - curHeight));
                        break;
                    case ScreenDockEdge.Top:
                    default:
                        targetLeft = Math.Clamp(_customLeft, 0, Math.Max(0, primaryWidth - curWidth));
                        targetTop = 0.0;
                        break;
                }
            }

            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);

            Left = targetLeft;
            Top = targetTop;
            Width = curWidth;
            Height = curHeight;

            UpdateShellDockBorders();

            if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
            {
                ExpandedContent.Visibility = Visibility.Visible;
                ExpandedContent.Opacity = 1.0;
                BrandPanel.Visibility = Visibility.Visible;
                BrandPanel.Opacity = 1.0;
                HeaderControls.Visibility = Visibility.Visible;
                HeaderControls.Opacity = 1.0;
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                IslandPomodoroPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(36);
                MainContentRow.Height = new GridLength(1, GridUnitType.Star);
                IslandBar.Height = 36;
                IslandBar.VerticalAlignment = VerticalAlignment.Top;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Top;
                MascotCapsule.Margin = new Thickness(0);
                MascotCapsule.Width = 96;
                MascotCapsule.Height = 34;
                MascotCapsule.Background = (Brush)FindResource("DarkIslandBrush");
                MascotCapsule.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                MascotCapsule.BorderThickness = new Thickness(1, 0, 1, 1);
                MascotCapsule.CornerRadius = new CornerRadius(0, 0, 14, 14);
                Shell.Background = GetCurrentShellBackground();
                Shell.BorderBrush = GetCurrentShellBorderBrush();
            }
            else
            {
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                BrandPanel.Visibility = Visibility.Collapsed;
                BrandPanel.Opacity = 0.0;
                HeaderControls.Visibility = Visibility.Collapsed;
                HeaderControls.Opacity = 0.0;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;

                if (_currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused)
                {
                    MusicActiveCompactPanel.Visibility = Visibility.Visible;
                    IslandPomodoroPanel.Visibility = Visibility.Collapsed;
                    MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                    MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                    MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                    MascotCapsule.Width = 44;
                    MascotCapsule.Height = 28;
                }
                else if (_currentState == DynamicIslandState.PomodoroActive)
                {
                    MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                    IslandPomodoroPanel.Visibility = Visibility.Visible;
                    MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                    MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                    MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                    MascotCapsule.Width = 44;
                    MascotCapsule.Height = 28;
                }
                else
                {
                    MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                    IslandPomodoroPanel.Visibility = Visibility.Collapsed;
                    MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                    MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                    MascotCapsule.Margin = new Thickness(0);
                    MascotCapsule.Width = 48;
                    MascotCapsule.Height = 28;
                }
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
            }

            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }

            if (Opacity < 0.1)
            {
                Opacity = 1.0;
            }

            if (forceTopmost)
            {
                Topmost = true;
                BringToTopmostSafe();
            }

            LogDiagnostic($"PositionNotch(State={_currentState}, W={Width:F0}, H={Height:F0}, L={Left:F0}, T={Top:F0})");
        }
        catch (Exception ex)
        {
            LogDiagnostic("PositionNotch-Error", ex);
        }
    }

    public void TriggerDiagnosticRecovery(bool toggleVisual = false)
    {
        try
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;

            if (Visibility != Visibility.Visible)
                Visibility = Visibility.Visible;

            if (Opacity < 0.2)
                Opacity = 1.0;

            SetExpanded(false, isUserPin: false);
            PositionNotch(forceTopmost: true);
            Activate();

            if (toggleVisual)
            {
                ToggleVisualDiagnosticsMode();
            }

            LogDiagnostic("TriggerDiagnosticRecovery executed");
        }
        catch (Exception ex)
        {
            LogDiagnostic("TriggerDiagnosticRecovery-Error", ex);
        }
    }

    public void ToggleVisualDiagnosticsMode()
    {
        try
        {
            _visualDiagnosticModeActive = !_visualDiagnosticModeActive;
            if (_visualDiagnosticModeActive)
            {
                // High-visibility lavender fallback background
                Shell.Background = new SolidColorBrush(Color.FromRgb(215, 195, 255));
                Shell.BorderBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
                Shell.BorderThickness = new Thickness(2.0);
                MiniStatusText.Text = "★ DIAGNOSTIC NOTCH ★";
                LogDiagnostic("ToggleVisualDiagnosticsMode: Enabled (High-visibility lavender mode)");
            }
            else
            {
                // Restore primary glassmorphism theme according to active DarkMode setting
                Shell.Background = GetCurrentShellBackground();
                Shell.BorderBrush = GetCurrentShellBorderBrush();
                Shell.BorderThickness = _originalShellBorderThickness.Top > 0 ? _originalShellBorderThickness : new Thickness(1.5);
                MiniStatusText.Text = "piko is ready";
                LogDiagnostic("ToggleVisualDiagnosticsMode: Disabled (Restored white-purple glassmorphism)");
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("ToggleVisualDiagnosticsMode-Error", ex);
        }
    }

    private void LogDiagnostic(string stage, Exception? ex = null)
    {
        try
        {
            var logFile = Path.Combine(Path.GetTempPath(), "piko_debug.log");
            var workArea = SystemParameters.WorkArea;
            var vLeft = SystemParameters.VirtualScreenLeft;
            var vTop = SystemParameters.VirtualScreenTop;
            var vWidth = SystemParameters.VirtualScreenWidth;
            var vHeight = SystemParameters.VirtualScreenHeight;

            var curW = ActualWidth > 0 ? ActualWidth : Width;
            var curH = ActualHeight > 0 ? ActualHeight : Height;

            var isInsideWorkArea = (Left >= workArea.Left - 1 &&
                                    Top >= workArea.Top - 1 &&
                                    Left + curW <= workArea.Right + 1 &&
                                    Top + curH <= workArea.Bottom + 1);

            var isInsideVirtual = (Left >= vLeft - 1 &&
                                   Top >= vTop - 1 &&
                                   Left + curW <= vLeft + vWidth + 1 &&
                                   Top + curH <= vTop + vHeight + 1);

            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}]");
            sb.AppendLine($"  Visibility: {Visibility}, IsVisible: {IsVisible}, Opacity: {Opacity:F2}, WindowState: {WindowState}");
            sb.AppendLine($"  Left: {Left:F1}, Top: {Top:F1}, Width: {Width:F1}, Height: {Height:F1}, ActualWidth: {ActualWidth:F1}, ActualHeight: {ActualHeight:F1}");
            sb.AppendLine($"  Topmost: {Topmost}, IsLoaded: {IsLoaded}");
            sb.AppendLine($"  Screen: Primary=({SystemParameters.PrimaryScreenWidth:F0}x{SystemParameters.PrimaryScreenHeight:F0}), WorkArea=({workArea.Left:F0},{workArea.Top:F0},{workArea.Width:F0}x{workArea.Height:F0}), Virtual=({vLeft:F0},{vTop:F0},{vWidth:F0}x{vHeight:F0})");
            sb.AppendLine($"  LocationRelative: InsideWorkArea={isInsideWorkArea}, InsideVirtualScreen={isInsideVirtual}");

            if (ex != null)
            {
                sb.AppendLine($"  EXCEPTION: {ex}");
            }

            File.AppendAllText(logFile, sb.ToString() + Environment.NewLine);
        }
        catch
        {
            // Do not let diagnostic logging failure crash the app
        }
    }

    // ---------------------------------------------------------
    // DYNAMIC ISLAND STATE MACHINE & USER INTERACTIONS
    // ---------------------------------------------------------

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_currentState == DynamicIslandState.FileEating) return;

        _collapseTimer.Stop();
        _sleepyTimer.Stop();
        _sleepyTimer.Start();

        if (_currentState == DynamicIslandState.Sleepy)
        {
            TransitionToState(DynamicIslandState.Hover);
            return;
        }

        if (_currentState == DynamicIslandState.Idle)
        {
            TransitionToState(DynamicIslandState.Hover);
            return;
        }

        if (_currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused)
        {
            // Hover preview: reveal play/pause button and expand notch smoothly
            MiniPlayPauseBtn.Visibility = Visibility.Visible;
            AnimateIsland(MusicHoverWidth, MusicActiveHeight, _currentState);
            return;
        }
    }

    private void HoverOpenTimer_Tick(object? sender, EventArgs e)
    {
        _hoverOpenTimer.Stop();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_currentState == DynamicIslandState.FileEating) return;

        _eyeController?.ResetTracking();

        if (_currentState == DynamicIslandState.Hover)
        {
            var returnState = _focusRunning ? DynamicIslandState.PomodoroActive :
                              ((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            TransitionToState(returnState);
            return;
        }

        if (_currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused)
        {
            MiniPlayPauseBtn.Visibility = Visibility.Collapsed;
            AnimateIsland(MusicActiveWidth, MusicActiveHeight, _currentState);
            return;
        }

        // Auto-collapse dashboard on mouse leave only if NOT pinned and currently expanded
        if (!_isPinned && _currentState == DynamicIslandState.Expanded)
        {
            _collapseTimer.Stop();
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(_state.AutoCollapseDelayMs);
            _collapseTimer.Start();
        }
    }

    private void AnimateIslandWidth(double targetWidth, double targetHeight = IdleHeight)
    {
        AnimateIsland(targetWidth, targetHeight, _currentState);
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!IsMouseOver && !_isPinned && _currentState == DynamicIslandState.Expanded)
        {
            var returnState = _focusRunning ? DynamicIslandState.PomodoroActive :
                              ((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            TransitionToState(returnState);
        }
    }

    // ---------------------------------------------------------
    // DOUBLE-CLICK DRAG & SCREEN-EDGE DOCKING
    // ---------------------------------------------------------

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject? target)
    {
        if (target == null) return false;
        while (node != null)
        {
            if (node == target) return true;
            node = VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent;
        }
        return false;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child) ?? (child as FrameworkElement)?.Parent;
        }
        return null;
    }

    private bool IsDynamicIslandClickTarget(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            // Interactive buttons, textboxes, and clickable controls should handle their own clicks
            if (dep is Button || dep is TextBox || dep is CheckBox || dep is RadioButton ||
                FindParent<Button>(dep) != null || FindParent<TextBox>(dep) != null || FindParent<CheckBox>(dep) != null)
            {
                return false;
            }

            // When expanded, only allow drag/toggle from the MascotCapsule or IslandBar header area
            if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
            {
                if (IsDescendantOf(dep, ExpandedContent))
                {
                    return false;
                }
                return IsDescendantOf(dep, MascotCapsule) || IsDescendantOf(dep, IslandBar) || IsDescendantOf(dep, BrandPanel);
            }

            return true;
        }
        return false;
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsDynamicIslandClickTarget(e)) return;

        if (e.ClickCount >= 2)
        {
            _singleClickTimer.Stop();
            _isDoubleDown = true;
            _isEdgeDragging = false;
            _hasMovedBeyondThreshold = false;
            try
            {
                _dragStartCursor = PointToScreen(e.GetPosition(this));
            }
            catch
            {
                _dragStartCursor = new Point(0, 0);
            }
            _dragStartLeft = Left;
            _dragStartTop = Top;
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDoubleDown || e.LeftButton != MouseButtonState.Pressed) return;

        Point screenPix;
        try
        {
            screenPix = PointToScreen(e.GetPosition(this));
        }
        catch
        {
            return;
        }

        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double curX = screenPix.X / dpiScaleX;
        double curY = screenPix.Y / dpiScaleY;

        if (!_hasMovedBeyondThreshold)
        {
            double diffX = Math.Abs(screenPix.X - _dragStartCursor.X);
            double diffY = Math.Abs(screenPix.Y - _dragStartCursor.Y);
            if (diffX > 6 || diffY > 6)
            {
                _hasMovedBeyondThreshold = true;
                _isEdgeDragging = true;
                _singleClickTimer.Stop();
            }
        }

        if (_isEdgeDragging)
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            double curW = ActualWidth > 0 ? ActualWidth : Width;
            double curH = ActualHeight > 0 ? ActualHeight : Height;

            double distTop = Math.Max(0, curY);
            double distLeft = Math.Max(0, curX);
            double distRight = Math.Max(0, screenW - curX);

            if (distTop <= distLeft && distTop <= distRight)
            {
                _dockEdge = ScreenDockEdge.Top;
                Top = 0;
                Left = Math.Clamp(curX - curW / 2.0, 0, Math.Max(0, screenW - curW));
            }
            else if (distLeft < distTop && distLeft <= distRight)
            {
                _dockEdge = ScreenDockEdge.Left;
                Left = 0;
                Top = Math.Clamp(curY - curH / 2.0, 0, Math.Max(0, screenH - curH));
            }
            else
            {
                _dockEdge = ScreenDockEdge.Right;
                Left = Math.Max(0, screenW - curW);
                Top = Math.Clamp(curY - curH / 2.0, 0, Math.Max(0, screenH - curH));
            }

            _customLeft = Left;
            _customTop = Top;
            _isCustomPosition = true;

            UpdateShellDockBorders();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDoubleDown)
        {
            ReleaseMouseCapture();
            _isDoubleDown = false;

            if (_isEdgeDragging)
            {
                _isEdgeDragging = false;
                _state.DockEdge = _dockEdge.ToString();
                _state.CustomLeft = _customLeft;
                _state.CustomTop = _customTop;
                _state.IsCustomPosition = true;
                SaveAppState();
                _soundService.PlayGentleTap();
                LogDiagnostic($"Edge drag complete: DockEdge={_dockEdge}, Left={Left:F1}, Top={Top:F1}");
                e.Handled = true;
            }
            else
            {
                // Double-click without drag -> Reset to top-center
                ResetToTopCenterAnimated();
                e.Handled = true;
            }
        }
        else
        {
            if (IsDynamicIslandClickTarget(e) && !_isEdgeDragging)
            {
                _singleClickTimer.Stop();
                _singleClickTimer.Start();
            }
        }
    }

    private void ResetToTopCenterAnimated()
    {
        var primaryWidth = SystemParameters.PrimaryScreenWidth;
        var targetLeft = Math.Max(0, (primaryWidth - Width) / 2.0);
        var targetTop = 0.0;

        _dockEdge = ScreenDockEdge.Top;
        _isCustomPosition = false;
        _customLeft = targetLeft;
        _customTop = targetTop;

        _state.DockEdge = "Top";
        _state.IsCustomPosition = false;
        _state.CustomLeft = targetLeft;
        _state.CustomTop = targetTop;
        SaveAppState();

        var duration = _state.ReducedMotion ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(380);
        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };

        var leftAnim = new DoubleAnimation(Left, targetLeft, duration) { EasingFunction = ease };
        var topAnim = new DoubleAnimation(Top, targetTop, duration) { EasingFunction = ease };

        topAnim.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            UpdateShellDockBorders();
            BringToTopmostSafe();
        };

        BeginAnimation(LeftProperty, leftAnim);
        BeginAnimation(TopProperty, topAnim);

        UpdateShellDockBorders();
        _soundService.PlaySuccessChime();
        _eyeController?.TriggerBounce();
        LogDiagnostic("ResetToTopCenterAnimated executed");
    }

    private void MascotCapsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e) => ToggleExpand();

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        _currentState = _isPinned ? DynamicIslandState.Pinned : DynamicIslandState.Expanded;
        UpdateExpandButtonState();
        LogDiagnostic($"Panel pin toggled by user button: _isPinned={_isPinned}");
    }

    private void ToggleExpand()
    {
        _hoverOpenTimer.Stop();
        _collapseTimer.Stop();

        if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
        {
            // Currently open -> COLLAPSE back to true active state
            _isPinned = false;
            var returnState = _focusRunning ? DynamicIslandState.PomodoroActive :
                              ((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            TransitionToState(returnState);
        }
        else
        {
            // Currently collapsed (Idle, Sleepy, MusicActive, PomodoroActive, Hover, etc.) -> EXPAND
            _isPinned = true;
            if (_isMediaPlaying || _demoPlaying)
            {
                TabMusic.IsChecked = true;
                UpdateTabPanels();
            }
            TransitionToState(DynamicIslandState.Pinned);
        }
    }

    public void SetExpanded(bool expand, bool isUserPin = false)
    {
        if (expand)
        {
            _isPinned = isUserPin;
            TransitionToState(isUserPin ? DynamicIslandState.Pinned : DynamicIslandState.Expanded);
        }
        else
        {
            _isPinned = false;
            var returnState = _focusRunning ? DynamicIslandState.PomodoroActive :
                              ((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            TransitionToState(returnState);
        }
    }

    private void DismissActivity_Click(object sender, RoutedEventArgs e)
    {
        DismissCurrentActivity();
    }

    private void SuccessPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _singleClickTimer.Stop();
        e.Handled = true;
        TabSettings.IsChecked = true;
        UpdateTabPanels();
        TransitionToState(DynamicIslandState.Pinned);
    }

    private void MusicPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _singleClickTimer.Stop();
        e.Handled = true;
        TabMusic.IsChecked = true;
        UpdateTabPanels();
        TransitionToState(DynamicIslandState.Pinned);
    }

    private void ActivityAutoCollapseTimer_Tick(object? sender, EventArgs e)
    {
        _activityAutoCollapseTimer.Stop();

        // If the dashboard is currently expanded or pinned by user, do not force-collapse it!
        if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
        {
            return;
        }

        // If there are still active AI sessions running, transition back to AiWorking for remaining AIs
        if (_activeAiSessions.Count > 0)
        {
            var activeKeys = _activeAiSessions.Keys.ToList();
            if (activeKeys.Count == 1)
            {
                var src = activeKeys[0];
                var info = _activeAiSessions[src];
                TransitionToState(DynamicIslandState.AiWorking, (src, info.Message));
            }
            else
            {
                var tag = $"{activeKeys.Count} AI";
                var text = $"[{string.Join(" + ", activeKeys)}] sedang berpikir...";
                TransitionToState(DynamicIslandState.AiWorking, (tag, text));
            }
            return;
        }

        if (_notificationQueue.Count > 0)
        {
            var next = _notificationQueue.Dequeue();
            TransitionToState(DynamicIslandState.Notification, next);
        }
        else if (_focusRunning)
        {
            TransitionToState(DynamicIslandState.PomodoroActive);
        }
        else if (_isMediaPlaying || _demoPlaying)
        {
            TransitionToState(DynamicIslandState.MusicActive);
        }
        else
        {
            TransitionToState(DynamicIslandState.Idle);
        }
    }

    private void AnimateEqBar(Border bar, double targetHeight)
    {
        if (_state.ReducedMotion)
        {
            bar.Height = targetHeight;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(95),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        bar.BeginAnimation(HeightProperty, anim);
    }

    private void EqualizerTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentState != DynamicIslandState.MusicActive || _state.ReducedMotion) return;
        _eqTick = (_eqTick + 1) % 6;
        double h1 = _eqTick switch { 0 => 5, 1 => 11, 2 => 8, 3 => 13, 4 => 6, _ => 9 };
        double h2 = _eqTick switch { 0 => 13, 1 => 7, 2 => 15, 3 => 9, 4 => 14, _ => 11 };
        double h3 = _eqTick switch { 0 => 7, 1 => 14, 2 => 6, 3 => 11, 4 => 8, _ => 13 };
        double h4 = _eqTick switch { 0 => 15, 1 => 8, 2 => 12, 3 => 16, 4 => 10, _ => 7 };
        double h5 = _eqTick switch { 0 => 6, 1 => 12, 2 => 9, 3 => 5, 4 => 11, _ => 8 };

        AnimateEqBar(EqBar1, h1);
        AnimateEqBar(EqBar2, h2);
        AnimateEqBar(EqBar3, h3);
        AnimateEqBar(EqBar4, h4);
        AnimateEqBar(EqBar5, h5);

        double dh1 = _eqTick switch { 0 => 8, 1 => 13, 2 => 6, 3 => 14, 4 => 9, _ => 11 };
        double dh2 = _eqTick switch { 0 => 14, 1 => 8, 2 => 15, 3 => 10, 4 => 16, _ => 12 };
        double dh3 = _eqTick switch { 0 => 7, 1 => 12, 2 => 14, 3 => 8, 4 => 13, _ => 9 };
        AnimateEqBar(DetailedEqBar1, dh1);
        AnimateEqBar(DetailedEqBar2, dh2);
        AnimateEqBar(DetailedEqBar3, dh3);
    }

    private void SleepyTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentState == DynamicIslandState.Idle &&
            !IsMouseOver &&
            !_isMediaPlaying &&
            !_demoPlaying &&
            !_stateManager.HasActiveNotification &&
            !_isPinned &&
            _notificationQueue.Count == 0)
        {
            TransitionToState(DynamicIslandState.Sleepy);
        }
    }

    public void TriggerIslandNotification(IslandNotificationItem item)
    {
        Dispatcher.Invoke(() =>
        {
            if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
            {
                MiniStatusText.Text = $"{item.Icon} {item.Title}";
                _notificationQueue.Enqueue(item);
                return;
            }

            if (_currentState == DynamicIslandState.AiWorking && item.Priority < NotificationPriority.Ai)
            {
                _notificationQueue.Enqueue(item);
                return;
            }

            if (_currentState == DynamicIslandState.Success && item.Priority < NotificationPriority.Ai)
            {
                _notificationQueue.Enqueue(item);
                return;
            }

            TransitionToState(DynamicIslandState.Notification, item);
        });
    }

    private void DismissCurrentActivity()
    {
        _activityAutoCollapseTimer.Stop();
        if (_notificationQueue.Count > 0)
        {
            var next = _notificationQueue.Dequeue();
            TransitionToState(DynamicIslandState.Notification, next);
        }
        else if (_focusRunning)
        {
            TransitionToState(DynamicIslandState.PomodoroActive);
        }
        else if (_isMediaPlaying || _demoPlaying)
        {
            TransitionToState(DynamicIslandState.MusicActive);
        }
        else
        {
            TransitionToState(DynamicIslandState.Idle);
        }
    }

    private void ShowMusicActivity()
    {
        var track = _demoTracks[_trackIndex];
        _sharedMedia.Source = "Demo";
        _sharedMedia.Title = track.Title;
        _sharedMedia.Channel = track.Artist;
        _sharedMedia.PlaybackState = "playing";
        _sharedMedia.ThumbnailUrl = "";
        _sharedMedia.Duration = 244;
        _sharedMedia.CurrentTime = 30;
        SyncSharedMediaUI();

        if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned
            and not DynamicIslandState.AiWorking and not DynamicIslandState.Success
            and not DynamicIslandState.Notification and not DynamicIslandState.FileEating)
        {
            if (_focusRunning)
            {
                TransitionToState(DynamicIslandState.PomodoroActive);
            }
            else
            {
                TransitionToState(DynamicIslandState.MusicActive);
            }
        }
    }

    private void StopMusicActivity()
    {
        _demoPlaying = false;
        IdleMusicDot.Visibility = Visibility.Collapsed;
        _sharedMedia.Reset();
        SyncSharedMediaUI();
        if (_currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused)
        {
            TransitionToState(DynamicIslandState.Idle);
        }
    }

    public void TransitionToState(DynamicIslandState targetState, object? payload = null)
    {
        _currentState = targetState;
        _hoverOpenTimer.Stop();
        _collapseTimer.Stop();
        _activityAutoCollapseTimer.Stop();
        _sleepyTimer.Stop();
        _sleepyTimer.Start();

        // Ensure Sleepy Zzz loop is stopped whenever transitioning out of Sleepy
        if (targetState != DynamicIslandState.Sleepy)
        {
            if (_zzzStoryboard != null)
            {
                try { _zzzStoryboard.Stop(this); } catch { }
            }
            ZzzCanvas.Visibility = Visibility.Collapsed;
            Zzz1.Opacity = 0;
            Zzz2.Opacity = 0;
            Zzz3.Opacity = 0;
        }

        double targetWidth;
        double targetHeight;

        Shell.BorderThickness = new Thickness(1, 0, 1, 1);
        IslandPomodoroPanel.Visibility = Visibility.Collapsed;
        IslandWaterReminderPanel.Visibility = Visibility.Collapsed;

        switch (targetState)
        {
            case DynamicIslandState.Idle:
                targetWidth = IdleWidth;
                targetHeight = IdleHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(0);
                MascotCapsule.Width = 48;
                MascotCapsule.Height = 28;
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                _stateManager.SetEmotion(PikoEmotion.Idle);
                _eyeController?.ApplyEmotion(PikoEmotion.Idle, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.Hover:
                targetWidth = HoverWidth;
                targetHeight = HoverHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(0);
                MascotCapsule.Width = 48;
                MascotCapsule.Height = 28;
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _stateManager.SetEmotion(PikoEmotion.Hover);
                _eyeController?.ApplyEmotion(PikoEmotion.Hover, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.PomodoroActive:
                targetWidth = PomodoroActiveWidth;
                targetHeight = PomodoroActiveHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                MascotCapsule.Width = 44;
                MascotCapsule.Height = 28;
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                IslandPomodoroPanel.Visibility = Visibility.Visible;
                IslandWaterReminderPanel.Visibility = Visibility.Collapsed;
                MiniPlayPauseBtn.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                _stateManager.SetEmotion(PikoEmotion.Working);
                _eyeController?.ApplyEmotion(PikoEmotion.Working, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.WaterReminder:
                targetWidth = 310;
                targetHeight = 36;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                MascotCapsule.Width = 44;
                MascotCapsule.Height = 28;
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                IslandPomodoroPanel.Visibility = Visibility.Collapsed;
                IslandWaterReminderPanel.Visibility = Visibility.Visible;
                MiniPlayPauseBtn.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 0, 229, 255));
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                _stateManager.SetEmotion(PikoEmotion.Thirsty);
                _eyeController?.ApplyEmotion(PikoEmotion.Thirsty, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.Sleepy:
                if (_isMediaPlaying || _demoPlaying)
                {
                    LogDiagnostic("TransitionToState(Sleepy) ignored: media is actively playing");
                    return;
                }

                targetWidth = SleepyWidth;
                targetHeight = SleepyHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(0);
                MascotCapsule.Width = 48;
                MascotCapsule.Height = 28;
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _stateManager.SetEmotion(PikoEmotion.Sleepy);
                _eyeController?.ApplyEmotion(PikoEmotion.Sleepy, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);

                // Start gentle Zzz floating animation loop
                ZzzCanvas.Visibility = Visibility.Visible;
                try
                {
                    _zzzStoryboard ??= (Storyboard)FindResource("ZzzSleepStoryboard");
                    _zzzStoryboard.Begin(this, isControllable: true);
                }
                catch (Exception ex)
                {
                    LogDiagnostic("ZzzStoryboard-Error", ex);
                }
                break;

            case DynamicIslandState.MusicActive:
                targetWidth = MusicActiveWidth;
                targetHeight = MusicActiveHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                MascotCapsule.Width = 44;
                MascotCapsule.Height = 28;
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Visible;
                MiniPlayPauseBtn.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                Shell.CornerRadius = new CornerRadius(0, 0, 22, 22);
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                _stateManager.SetEmotion(PikoEmotion.Listening);
                _eyeController?.ApplyEmotion(PikoEmotion.Listening, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(true, isPlaying: true);
                _equalizerTimer.Start();
                EqBar1.Opacity = 1.0; EqBar2.Opacity = 1.0; EqBar3.Opacity = 1.0; EqBar4.Opacity = 1.0; EqBar5.Opacity = 1.0;
                EqualizerTimer_Tick(null, EventArgs.Empty);
                break;

            case DynamicIslandState.MediaPaused:
                targetWidth = MusicActiveWidth;
                targetHeight = MusicActiveHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                TopHeaderRow.Height = new GridLength(1, GridUnitType.Star);
                MainContentRow.Height = new GridLength(0);
                IslandBar.Height = double.NaN;
                IslandBar.VerticalAlignment = VerticalAlignment.Stretch;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                MascotCapsule.Width = 44;
                MascotCapsule.Height = 28;
                MascotCapsule.Background = Brushes.Transparent;
                MascotCapsule.BorderThickness = new Thickness(0);
                ActivityPanel.Visibility = Visibility.Collapsed;
                MusicActiveCompactPanel.Visibility = Visibility.Visible;
                MiniPlayPauseBtn.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                Shell.Background = (Brush)FindResource("DarkIslandBrush");
                Shell.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                _equalizerTimer.Stop();
                AnimateEqBar(EqBar1, 3); AnimateEqBar(EqBar2, 3); AnimateEqBar(EqBar3, 3); AnimateEqBar(EqBar4, 3); AnimateEqBar(EqBar5, 3);
                AnimateEqBar(DetailedEqBar1, 3); AnimateEqBar(DetailedEqBar2, 3); AnimateEqBar(DetailedEqBar3, 3);
                EqBar1.Opacity = 0.35; EqBar2.Opacity = 0.35; EqBar3.Opacity = 0.35; EqBar4.Opacity = 0.35; EqBar5.Opacity = 0.35;
                _stateManager.SetEmotion(PikoEmotion.Idle);
                _eyeController?.ApplyEmotion(PikoEmotion.Idle, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(true, isPlaying: false);
                break;

            case DynamicIslandState.AiWorking:
                targetWidth = _activeAiSessions.Count > 1 ? 320 : AiWorkingWidth;
                targetHeight = AiWorkingHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.Margin = new Thickness(4, 0, 0, 0);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                IslandWaterReminderPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Visible;
                SuccessPanel.Visibility = Visibility.Collapsed;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Collapsed;
                FileEatingPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                string workingSource = "AI";
                if (payload is (string src, string msg))
                {
                    workingSource = src;
                    AiWorkingText.Text = msg;
                }
                else if (payload is ValueTuple<string, string> vt)
                {
                    workingSource = vt.Item1;
                    AiWorkingText.Text = vt.Item2;
                }
                else if (payload is string s)
                {
                    AiWorkingText.Text = s;
                    if (s.Contains("Gemini", StringComparison.OrdinalIgnoreCase)) workingSource = "Gemini";
                    else if (s.Contains("Claude", StringComparison.OrdinalIgnoreCase)) workingSource = "Claude";
                    else if (s.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) workingSource = "DeepSeek";
                    else if (s.Contains("Antigravity", StringComparison.OrdinalIgnoreCase)) workingSource = "Antigravity";
                    else if (s.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)) workingSource = "ChatGPT";
                }
                if (AiWorkingTag != null)
                {
                    AiWorkingTag.Text = workingSource;
                }
                _stateManager.SetEmotion(PikoEmotion.Working);
                _eyeController?.ApplyEmotion(PikoEmotion.Working, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.Success:
                targetWidth = SuccessWidth;
                targetHeight = SuccessHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.Margin = new Thickness(4, 0, 0, 0);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                IslandWaterReminderPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Collapsed;
                SuccessPanel.Visibility = Visibility.Visible;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Collapsed;
                FileEatingPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                if (payload is (string succSrc, string succSub))
                {
                    if (SuccessTitle != null) SuccessTitle.Text = $"{succSrc} Selesai! ✨";
                    if (SuccessSubtext != null) SuccessSubtext.Text = succSub;
                }
                else if (payload is ValueTuple<string, string> succVt)
                {
                    if (SuccessTitle != null) SuccessTitle.Text = $"{succVt.Item1} Selesai! ✨";
                    if (SuccessSubtext != null) SuccessSubtext.Text = succVt.Item2;
                }
                else if (payload is string sub)
                {
                    if (SuccessSubtext != null) SuccessSubtext.Text = sub;
                }
                _stateManager.SetEmotion(PikoEmotion.Success);
                _eyeController?.ApplyEmotion(PikoEmotion.Success, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                _soundService.PlaySuccessChime();

                // If other AIs are still running, show success briefly (2.2s) then revert to remaining AI
                _activityAutoCollapseTimer.Interval = _activeAiSessions.Count > 0 ? TimeSpan.FromSeconds(2.2) : TimeSpan.FromSeconds(4.0);
                _activityAutoCollapseTimer.Start();
                break;

            case DynamicIslandState.Notification:
                targetWidth = NotificationWidth;
                targetHeight = NotificationHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.Margin = new Thickness(4, 0, 0, 0);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Collapsed;
                SuccessPanel.Visibility = Visibility.Collapsed;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Visible;
                FileEatingPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                if (payload is IslandNotificationItem notif)
                {
                    NotifIcon.Text = notif.Icon;
                    NotifTitle.Text = notif.Title;
                    NotifBody.Text = notif.Message;
                    _activityAutoCollapseTimer.Interval = notif.Duration;
                }
                else
                {
                    _activityAutoCollapseTimer.Interval = TimeSpan.FromSeconds(5);
                }
                _activityAutoCollapseTimer.Start();
                _stateManager.SetEmotion(PikoEmotion.Alert);
                _eyeController?.ApplyEmotion(PikoEmotion.Alert, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.FileEating:
                targetWidth = FileEatingWidth;
                targetHeight = FileEatingHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.Margin = new Thickness(4, 0, 0, 0);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Collapsed;
                SuccessPanel.Visibility = Visibility.Collapsed;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Collapsed;
                FileEatingPanel.Visibility = Visibility.Visible;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                _stateManager.SetEmotion(PikoEmotion.Alert);
                _eyeController?.ApplyEmotion(PikoEmotion.Alert, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                break;

            case DynamicIslandState.Expanded:
            case DynamicIslandState.Pinned:
                targetWidth = ExpandedWidth;
                targetHeight = ExpandedHeight;
                BrandPanel.Visibility = Visibility.Visible;
                BrandPanel.Opacity = 0.0;
                TopHeaderRow.Height = new GridLength(36);
                MainContentRow.Height = new GridLength(1, GridUnitType.Star);
                IslandBar.Height = 36;
                IslandBar.VerticalAlignment = VerticalAlignment.Top;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Center;
                MascotCapsule.VerticalAlignment = VerticalAlignment.Top;
                MascotCapsule.Margin = new Thickness(0);
                MascotCapsule.Width = 96;
                MascotCapsule.Height = 34;
                MascotCapsule.Background = (Brush)FindResource("DarkIslandBrush");
                MascotCapsule.BorderBrush = (Brush)FindResource("DarkIslandBorderBrush");
                MascotCapsule.BorderThickness = new Thickness(1, 0, 1, 1);
                MascotCapsule.CornerRadius = new CornerRadius(0, 0, 14, 14);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Visible;
                HeaderControls.Opacity = 0.0;
                ExpandedContent.Visibility = Visibility.Visible;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.Background = GetCurrentShellBackground();
                Shell.BorderBrush = GetCurrentShellBorderBrush();
                _equalizerTimer.Stop();
                _isPinned = (targetState == DynamicIslandState.Pinned);
                _panelState = NotchPanelState.Expanded;
                _eyeController?.ApplyEmotion(PikoEmotion.Idle, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                EnsureActiveTabValid();
                break;

            default:
                targetWidth = IdleWidth;
                targetHeight = IdleHeight;
                break;
        }

        UpdateExpandButtonState();
        AnimateIsland(targetWidth, targetHeight, targetState);
        LogDiagnostic($"TransitionToState: initiated {targetState} ({targetWidth}x{targetHeight})");
    }

    private void AnimateIsland(double targetWidth, double targetHeight, DynamicIslandState targetState)
    {
        var primaryWidth = SystemParameters.PrimaryScreenWidth;
        var primaryHeight = SystemParameters.PrimaryScreenHeight;

        double targetLeft;
        double targetTop;

        if (!_isCustomPosition)
        {
            targetLeft = Math.Max(0, (primaryWidth - targetWidth) / 2.0);
            targetTop = 0.0;
        }
        else
        {
            switch (_dockEdge)
            {
                case ScreenDockEdge.Left:
                    targetLeft = 0.0;
                    targetTop = Math.Clamp(_customTop, 0, Math.Max(0, primaryHeight - targetHeight));
                    break;
                case ScreenDockEdge.Right:
                    targetLeft = Math.Max(0, primaryWidth - targetWidth);
                    targetTop = Math.Clamp(_customTop, 0, Math.Max(0, primaryHeight - targetHeight));
                    break;
                case ScreenDockEdge.Top:
                default:
                    targetLeft = Math.Clamp(_customLeft, 0, Math.Max(0, primaryWidth - targetWidth));
                    targetTop = 0.0;
                    break;
            }
        }

        // Premium spring-like durations per state
        var duration = _state.ReducedMotion
            ? TimeSpan.FromMilliseconds(1)
            : targetState switch
            {
                DynamicIslandState.Expanded or DynamicIslandState.Pinned => TimeSpan.FromMilliseconds(380),
                DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused => TimeSpan.FromMilliseconds(340),
                DynamicIslandState.Hover => TimeSpan.FromMilliseconds(220),
                DynamicIslandState.Idle => TimeSpan.FromMilliseconds(320),
                DynamicIslandState.Notification or DynamicIslandState.Success or DynamicIslandState.FileEating => TimeSpan.FromMilliseconds(300),
                _ => TimeSpan.FromMilliseconds(280)
            };

        // Differentiated premium easing per state for spring-like feel
        IEasingFunction easing = targetState switch
        {
            DynamicIslandState.Expanded or DynamicIslandState.Pinned =>
                new BackEase { Amplitude = 0.15, EasingMode = EasingMode.EaseOut },
            DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused =>
                new ExponentialEase { Exponent = 4, EasingMode = EasingMode.EaseOut },
            DynamicIslandState.Idle =>
                new CubicEase { EasingMode = EasingMode.EaseInOut },
            DynamicIslandState.Hover =>
                new QuadraticEase { EasingMode = EasingMode.EaseOut },
            DynamicIslandState.Notification or DynamicIslandState.Success or DynamicIslandState.FileEating =>
                new BackEase { Amplitude = 0.14, EasingMode = EasingMode.EaseOut },
            _ =>
                new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // Height uses a slightly softer easing for a 'breathe' effect
        IEasingFunction heightEasing = targetState switch
        {
            DynamicIslandState.Expanded or DynamicIslandState.Pinned =>
                new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut },
            _ => easing
        };

        double startWidth = ActualWidth > 0 ? ActualWidth : targetWidth;
        double startHeight = ActualHeight > 0 ? ActualHeight : targetHeight;
        double startLeft = !double.IsNaN(Left) ? Left : targetLeft;
        double startTop = !double.IsNaN(Top) ? Top : targetTop;

        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        Width = startWidth;
        Height = startHeight;
        Left = startLeft;
        Top = startTop;

        var widthAnim = new DoubleAnimation(startWidth, targetWidth, duration) { EasingFunction = easing };
        var heightAnim = new DoubleAnimation(startHeight, targetHeight, duration) { EasingFunction = heightEasing };
        var leftAnim = new DoubleAnimation(startLeft, targetLeft, duration) { EasingFunction = easing };
        var topAnim = new DoubleAnimation(startTop, targetTop, duration) { EasingFunction = easing };

        var isExpandingToDashboard = targetState is DynamicIslandState.Expanded or DynamicIslandState.Pinned;
        if (isExpandingToDashboard)
        {
            Shell.Background = GetCurrentShellBackground();
            Shell.BorderBrush = GetCurrentShellBorderBrush();

            BrandPanel.Visibility = Visibility.Visible;
            HeaderControls.Visibility = Visibility.Visible;
            ExpandedContent.Visibility = Visibility.Visible;

            BrandPanel.Opacity = 0.0;
            HeaderControls.Opacity = 0.0;
            ExpandedContent.Opacity = 0.0;

            var fadeInDuration = _state.ReducedMotion ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(240);
            var contentOpacityAnim = new DoubleAnimation(0.0, 1.0, fadeInDuration)
            {
                BeginTime = _state.ReducedMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ExpandedContent.BeginAnimation(OpacityProperty, contentOpacityAnim);

            // Right header controls (silang, pin, notif) & left brand fade in smoothly ONLY AFTER notch has expanded wide enough
            var headerFadeDuration = _state.ReducedMotion ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(180);
            var headerOpacityAnim = new DoubleAnimation(0.0, 1.0, headerFadeDuration)
            {
                BeginTime = _state.ReducedMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(170),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HeaderControls.BeginAnimation(OpacityProperty, headerOpacityAnim);
            BrandPanel.BeginAnimation(OpacityProperty, headerOpacityAnim);
        }
        else
        {
            // Collapsing back to island notch: fade out HeaderControls, BrandPanel, and ExpandedContent fast so they never appear in the small notch!
            var fadeOutDuration = _state.ReducedMotion ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(80);
            var fadeOutAnim = new DoubleAnimation(1.0, 0.0, fadeOutDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            ExpandedContent.BeginAnimation(OpacityProperty, fadeOutAnim);
            HeaderControls.BeginAnimation(OpacityProperty, fadeOutAnim);
            BrandPanel.BeginAnimation(OpacityProperty, fadeOutAnim);
        }

        heightAnim.Completed += (_, _) =>
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            ExpandedContent.BeginAnimation(OpacityProperty, null);
            HeaderControls.BeginAnimation(OpacityProperty, null);
            BrandPanel.BeginAnimation(OpacityProperty, null);

            Width = targetWidth;
            Height = targetHeight;
            Left = targetLeft;
            Top = targetTop;
            _currentState = targetState;
            _panelState = isExpandingToDashboard ? NotchPanelState.Expanded : NotchPanelState.Collapsed;

            UpdateShellDockBorders();

            if (isExpandingToDashboard)
            {
                ExpandedContent.Opacity = 1.0;
                ExpandedContent.Visibility = Visibility.Visible;
                HeaderControls.Opacity = 1.0;
                HeaderControls.Visibility = Visibility.Visible;
                BrandPanel.Opacity = 1.0;
                BrandPanel.Visibility = Visibility.Visible;
                Shell.Background = GetCurrentShellBackground();
                Shell.BorderBrush = GetCurrentShellBorderBrush();
                EnsureActiveTabValid();
            }
            else
            {
                ExpandedContent.Opacity = 0.0;
                ExpandedContent.Visibility = Visibility.Collapsed;
                HeaderControls.Opacity = 0.0;
                HeaderControls.Visibility = Visibility.Collapsed;
                BrandPanel.Opacity = 0.0;
                BrandPanel.Visibility = Visibility.Collapsed;
            }

            UpdateExpandButtonState();
            BringToTopmostSafe();
            LogDiagnostic($"TransitionCompleted(state={_currentState}, W={Width:F1}, H={Height:F1}, L={Left:F1}, T={Top:F1})");
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
        BeginAnimation(LeftProperty, leftAnim);
        BeginAnimation(TopProperty, topAnim);
    }

    private void UpdateExpandButtonState()
    {
        switch (_currentState)
        {
            case DynamicIslandState.Pinned:
                ExpandToggleMiniButton.Content = "📌";
                ExpandToggleMiniButton.ToolTip = "Unpin Panel (Klik untuk menutup)";
                break;

            case DynamicIslandState.Expanded:
                ExpandToggleMiniButton.Content = "▴";
                ExpandToggleMiniButton.ToolTip = "Pin Panel (Tetap Terbuka)";
                break;

            default:
                ExpandToggleMiniButton.Content = "▾";
                ExpandToggleMiniButton.ToolTip = "Buka Dashboard Piko";
                break;
        }
    }

    // ---------------------------------------------------------
    // MINIMAL LINE-EYES DYNAMIC ISLAND CONTROLLER
    // ---------------------------------------------------------

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (EyePair is null || Width <= 0 || Height <= 0) return;
        if (_stateManager.CurrentEmotion == PikoEmotion.Success || _stateManager.CurrentEmotion == PikoEmotion.Sleepy) return;

        var point = e.GetPosition(this);
        var normX = Math.Clamp((point.X / Width - 0.5) * 2.0, -1.0, 1.0);
        var normY = Math.Clamp((point.Y / Height - 0.5) * 2.0, -1.0, 1.0);

        _eyeController?.TrackCursor(normX, normY);
    }

    private async void BlinkTimer_Tick(object? sender, EventArgs e)
    {
        _blinkTimer.Stop();

        if (_eyeController != null)
        {
            await _eyeController.BlinkAsync();
        }

        _blinkTimer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(3000, 6800));
        if (IsLoaded) _blinkTimer.Start();
    }

    private void StateManager_EmotionChanged(PikoEmotion oldEmotion, PikoEmotion newEmotion)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyEmotionVisuals(newEmotion);
            HomeEmotionBadge.Text = newEmotion.GetEmotionBadge();
        });
    }

    private void StateManager_StatusChanged(string message)
    {
        Dispatcher.Invoke(() => MiniStatusText.Text = message);
    }

    private void StateManager_NotificationChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_stateManager.HasActiveNotification)
            {
                AINotificationTitle.Text = _stateManager.ActiveNotificationTitle ?? "AI Task Finished";
                AIStatusText.Text = _stateManager.ActiveNotificationBody ?? "Jawabanmu sudah siap.";
                AIStatusBadgeText.Text = "DONE ✨";
                AIStatusBadge.Background = new SolidColorBrush(Color.FromRgb(237, 229, 255));
                AIStatusBadgeText.Foreground = (Brush)FindResource("PrimaryVioletBrush");
            }
            else
            {
                AINotificationTitle.Text = "Your AI companion is on standby";
                AIStatusText.Text = "Buka ChatGPT, Gemini, Claude, atau DeepSeek di browser. Saat AI selesai berpikir, Piko akan memberi tahu seketika ✨";
                AIStatusBadgeText.Text = "STANDBY";
                AIStatusBadge.Background = (Brush)FindResource("SoftChipBrush");
                AIStatusBadgeText.Foreground = (Brush)FindResource("DeepVioletBrush");
            }
        });
    }

    private void ApplyEmotionVisuals(PikoEmotion emotion)
    {
        _workingAnimationTimer.Stop();
        _eyeController?.ApplyEmotion(emotion, _state.ReducedMotion);

        if (emotion == PikoEmotion.Working)
        {
            _workingAnimStep = 0;
            _workingAnimationTimer.Start();
        }
        else if (emotion == PikoEmotion.Success)
        {
            TriggerBounceAnimation();
            _soundService.PlaySuccessChime();
        }
        else if (emotion is PikoEmotion.Error or PikoEmotion.Alert)
        {
            TriggerBounceAnimation();
            _soundService.PlayAlertSound();
        }
    }

    private void WorkingAnimation_Tick(object? sender, EventArgs e)
    {
        if (_state.ReducedMotion) return;

        _workingAnimStep = (_workingAnimStep + 1) % 4;
        _eyeController?.WorkingTick(_workingAnimStep);

        if (AiThinkingDots != null)
        {
            AiThinkingDots.Text = (_workingAnimStep % 3) switch
            {
                0 => "•",
                1 => "••",
                _ => "•••"
            };
        }
    }

    private void TriggerBounceAnimation()
    {
        if (_state.ReducedMotion) return;
        _eyeController?.TriggerBounce();

        var bounce = new TranslateTransform();
        Shell.RenderTransform = bounce;
        var anim = new DoubleAnimation
        {
            From = 0,
            To = -5,
            Duration = TimeSpan.FromMilliseconds(120),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        bounce.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    public void HandleMediaPlayingWithPayload(string source, string title, BridgeMessage message)
    {
        LogDiagnostic($"[Media] Playing received with payload: title=\"{message.Title}\", channel=\"{message.Channel}\", videoId={message.VideoId}, tabId={message.TabId}");

        _isMediaPlaying = true;
        _demoPlaying = false;
        _mediaPauseTimer.Stop();

        _sharedMedia.Source = string.IsNullOrWhiteSpace(message.Source) ? "YouTube" : message.Source;
        _sharedMedia.Title = string.IsNullOrWhiteSpace(message.Title) ? (string.IsNullOrWhiteSpace(title) ? "Now Playing" : title) : message.Title;
        if (!string.IsNullOrWhiteSpace(message.Channel)) _sharedMedia.Channel = message.Channel;
        if (!string.IsNullOrWhiteSpace(message.VideoId)) _sharedMedia.VideoId = message.VideoId;
        if (!string.IsNullOrWhiteSpace(message.ThumbnailUrl)) _sharedMedia.ThumbnailUrl = message.ThumbnailUrl;
        if (!string.IsNullOrWhiteSpace(message.PageUrl)) _sharedMedia.PageUrl = message.PageUrl;
        if (message.Duration.HasValue && message.Duration.Value > 0) _sharedMedia.Duration = message.Duration.Value;
        if (message.CurrentTime.HasValue) _sharedMedia.CurrentTime = message.CurrentTime.Value;
        if (message.TabId.HasValue) _sharedMedia.TabId = message.TabId;
        _sharedMedia.PlaybackState = "playing";

        _mediaSource = _sharedMedia.Source;
        _mediaTitle = _sharedMedia.Title;

        // Synchronize all media UI
        SyncSharedMediaUI();

        // Priority check: Don't interrupt AI Working or Success notifications
        if (_currentState is DynamicIslandState.AiWorking or DynamicIslandState.Success or DynamicIslandState.Notification or DynamicIslandState.Pinned or DynamicIslandState.Expanded)
        {
            LogDiagnostic($"HandleMediaPlaying deferred due to active state {_currentState}");
            return;
        }

        // Debounce check: If already in MusicActive, ensure equalizer is running and return without restarting animations
        if (_currentState == DynamicIslandState.MusicActive)
        {
            if (!_equalizerTimer.IsEnabled) _equalizerTimer.Start();
            return;
        }

        LogDiagnostic("Piko state changed: MusicActive");
        TransitionToState(DynamicIslandState.MusicActive);
    }

    public void HandleMediaPlaying(string source, string title)
    {
        LogDiagnostic($"YouTube media detected: playing (source={source}, title={title})");
        _isMediaPlaying = true;
        _demoPlaying = false;
        _mediaSource = string.IsNullOrWhiteSpace(source) ? "YouTube" : source;
        _mediaTitle = string.IsNullOrWhiteSpace(title) ? "Now Playing" : title;

        _sharedMedia.Source = _mediaSource;
        _sharedMedia.Title = _mediaTitle;
        _sharedMedia.PlaybackState = "playing";

        SyncSharedMediaUI();
        _mediaPauseTimer.Stop();

        // Priority check: Don't interrupt AI Working or Success notifications
        if (_currentState is DynamicIslandState.AiWorking or DynamicIslandState.Success or DynamicIslandState.Notification or DynamicIslandState.Pinned or DynamicIslandState.Expanded)
        {
            LogDiagnostic($"HandleMediaPlaying deferred due to active state {_currentState}");
            return;
        }

        // Debounce check: If already in MusicActive, ensure equalizer is running and return without restarting animations
        if (_currentState == DynamicIslandState.MusicActive)
        {
            if (!_equalizerTimer.IsEnabled) _equalizerTimer.Start();
            return;
        }

        LogDiagnostic("Piko state changed: MusicActive");
        TransitionToState(DynamicIslandState.MusicActive);
    }

    public void HandleMediaMetadata(BridgeMessage message)
    {
        LogDiagnostic($"[Media] Metadata changed: title=\"{message.Title}\", channel=\"{message.Channel}\", videoId={message.VideoId}, state={message.PlaybackState}");

        // If another tab sent metadata while this tab is playing different video, ensure we don't clobber active playing tab
        if (_sharedMedia.IsActive && message.TabId.HasValue && _sharedMedia.TabId.HasValue && message.TabId != _sharedMedia.TabId && message.PlaybackState != "playing")
        {
            LogDiagnostic($"[Media] Ignored metadata from inactive background tab {message.TabId}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.Source)) _sharedMedia.Source = message.Source;
        if (!string.IsNullOrWhiteSpace(message.Title)) _sharedMedia.Title = message.Title;
        if (!string.IsNullOrWhiteSpace(message.Channel)) _sharedMedia.Channel = message.Channel;
        if (!string.IsNullOrWhiteSpace(message.VideoId)) _sharedMedia.VideoId = message.VideoId;
        if (!string.IsNullOrWhiteSpace(message.ThumbnailUrl)) _sharedMedia.ThumbnailUrl = message.ThumbnailUrl;
        if (!string.IsNullOrWhiteSpace(message.PageUrl)) _sharedMedia.PageUrl = message.PageUrl;
        if (message.Duration.HasValue && message.Duration.Value > 0) _sharedMedia.Duration = message.Duration.Value;
        if (message.CurrentTime.HasValue) _sharedMedia.CurrentTime = message.CurrentTime.Value;
        if (message.TabId.HasValue) _sharedMedia.TabId = message.TabId;
        if (!string.IsNullOrWhiteSpace(message.PlaybackState)) _sharedMedia.PlaybackState = message.PlaybackState;

        _mediaSource = _sharedMedia.Source;
        _mediaTitle = _sharedMedia.Title;

        // Synchronize all media UI
        SyncSharedMediaUI();

        // If currently in MusicActive, keep equalizer running seamlessly
        if (_currentState == DynamicIslandState.MusicActive && !_equalizerTimer.IsEnabled)
        {
            _equalizerTimer.Start();
        }
    }

    public void HandleMediaPausedWithPayload(string source, BridgeMessage message)
    {
        if (_sharedMedia.IsActive && message.TabId.HasValue && _sharedMedia.TabId.HasValue && message.TabId != _sharedMedia.TabId)
        {
            LogDiagnostic($"[Media] Ignored pause from inactive background tab {message.TabId} while active tab {_sharedMedia.TabId} is playing");
            return;
        }
        HandleMediaPaused(source);
    }

    public void HandleMediaPaused(string source)
    {
        LogDiagnostic($"YouTube media detected: paused (source={source})");
        _demoPlaying = false;
        _sharedMedia.PlaybackState = "paused";
        SyncSharedMediaUI();

        if (_currentState == DynamicIslandState.MusicActive)
        {
            LogDiagnostic("Piko state changed: MediaPaused");
            TransitionToState(DynamicIslandState.MediaPaused);
            _mediaPauseTimer.Stop();
            _mediaPauseTimer.Start();
        }
    }

    public void HandleMediaStoppedWithPayload(string source, BridgeMessage message)
    {
        if (_sharedMedia.IsActive && message.TabId.HasValue && _sharedMedia.TabId.HasValue && message.TabId != _sharedMedia.TabId)
        {
            LogDiagnostic($"[Media] Ignored stop from inactive background tab {message.TabId} while active tab {_sharedMedia.TabId} is playing");
            return;
        }
        HandleMediaStopped(source);
    }

    public void HandleMediaStopped(string source)
    {
        LogDiagnostic($"YouTube media detected: stopped (source={source})");
        _isMediaPlaying = false;
        _demoPlaying = false;
        _mediaPauseTimer.Stop();
        _sharedMedia.Reset();
        SyncSharedMediaUI();

        if (_currentState is DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused)
        {
            LogDiagnostic("Piko state changed: Idle (media stopped)");
            LogDiagnostic("Headphone visual disabled");
            TransitionToState(DynamicIslandState.Idle);
        }
    }

    private void SyncSharedMediaUI()
    {
        try
        {
            if (_sharedMedia.HasMedia)
            {
                var isPlaying = _sharedMedia.PlaybackState == "playing";
                var statusSuffix = isPlaying ? "Playing" : "Paused";

                // 1. Dynamic Island Notch
                MiniTrackTitle.Text = string.IsNullOrWhiteSpace(_sharedMedia.Title) ? "Now Playing" : _sharedMedia.Title;
                MiniTrackArtist.Text = $"{_sharedMedia.Source} · {statusSuffix}";
                IslandTrackTitle.Text = MiniTrackTitle.Text;
                IslandTrackArtist.Text = !string.IsNullOrWhiteSpace(_sharedMedia.Channel)
                    ? $"{_sharedMedia.Channel} · {_sharedMedia.Source}"
                    : MiniTrackArtist.Text;

                MiniPlayPauseBtn.Content = isPlaying ? "⏸" : "▶";

                // 2. Tab Music (PanelMusic)
                TrackTitleText.Text = _sharedMedia.Title;
                TrackArtistText.Text = !string.IsNullOrWhiteSpace(_sharedMedia.Channel)
                    ? $"{_sharedMedia.Channel} · {_sharedMedia.Source}"
                    : _sharedMedia.Source;

                TrackModeBadgeText.Text = isPlaying
                    ? $"{_sharedMedia.Source.ToUpperInvariant()} · PLAYING"
                    : $"{_sharedMedia.Source.ToUpperInvariant()} · PAUSED";

                PlayPauseButton.Content = isPlaying ? "⏸ Pause" : "▶ Play";
                MediaStatusText.Text = isPlaying
                    ? $"Terhubung real-time ke {_sharedMedia.Source} di browser Google Chrome."
                    : $"Media dijeda di {_sharedMedia.Source}.";

                // Artwork
                if (!string.IsNullOrWhiteSpace(_sharedMedia.ThumbnailUrl) && Uri.TryCreate(_sharedMedia.ThumbnailUrl, UriKind.Absolute, out var uri))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = uri;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        TrackArtworkImage.Source = bmp;
                        TrackArtworkImage.Visibility = Visibility.Visible;
                        TrackArtworkFallback.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        TrackArtworkImage.Visibility = Visibility.Collapsed;
                        TrackArtworkFallback.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    TrackArtworkImage.Visibility = Visibility.Collapsed;
                    TrackArtworkFallback.Visibility = Visibility.Visible;
                }

                // Progress Bar
                if (_sharedMedia.Duration > 0)
                {
                    TrackProgressBar.Maximum = _sharedMedia.Duration;
                    TrackProgressBar.Value = Math.Min(_sharedMedia.CurrentTime, _sharedMedia.Duration);
                    TrackCurrentTimeText.Text = _sharedMedia.FormatTime(_sharedMedia.CurrentTime);
                    TrackDurationText.Text = _sharedMedia.FormatTime(_sharedMedia.Duration);
                    TrackProgressGrid.Visibility = Visibility.Visible;
                }
                else
                {
                    TrackProgressBar.Value = 0;
                    TrackCurrentTimeText.Text = "0:00";
                    TrackDurationText.Text = "--:--";
                }

                // 3. Tab Home Preview Card
                HomeTrackText.Text = _sharedMedia.Title;
                HomeArtistText.Text = !string.IsNullOrWhiteSpace(_sharedMedia.Channel)
                    ? $"{_sharedMedia.Channel} · {_sharedMedia.Source}"
                    : _sharedMedia.Source;
                HomePlayPauseBtn.Content = isPlaying ? "⏸ Pause" : "▶ Play";
            }
            else
            {
                // Empty state when no media is playing
                MiniTrackTitle.Text = "Now Playing";
                MiniTrackArtist.Text = "YouTube · Standby";
                IslandTrackTitle.Text = "Belum ada media";
                IslandTrackArtist.Text = "Putar musik di Chrome";

                TrackTitleText.Text = "Belum ada media yang diputar";
                TrackArtistText.Text = "Putar musik atau video di YouTube Chrome";
                TrackModeBadgeText.Text = "STANDBY";
                PlayPauseButton.Content = "▶ Play";
                HomePlayPauseBtn.Content = "▶ Play";
                MediaStatusText.Text = "Belum ada media yang diputar. Putar video di YouTube Chrome untuk sinkronisasi otomatis.";

                TrackArtworkImage.Visibility = Visibility.Collapsed;
                TrackArtworkFallback.Visibility = Visibility.Visible;
                TrackProgressBar.Value = 0;
                TrackCurrentTimeText.Text = "0:00";
                TrackDurationText.Text = "0:00";

                HomeTrackText.Text = "Belum ada media";
                HomeArtistText.Text = "YouTube Standby";
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("SyncSharedMediaUI-Error", ex);
        }
    }

    private void MiniPlayPause_Click(object sender, RoutedEventArgs e)
    {
        PlayPauseButton_Click(sender, e);
    }

    // ---------------------------------------------------------
    // LOCAL BRIDGE & CHATGPT NOTIFICATIONS
    // ---------------------------------------------------------

    private void BridgeService_MessageReceived(BridgeMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            switch (msg.Type)
            {
                case BridgeMessage.TypeStart:
                case BridgeMessage.TypeGenerationStarted:
                    var startSource = string.IsNullOrWhiteSpace(msg.Source) ? "AI" : msg.Source;
                    var startTitle = string.IsNullOrWhiteSpace(msg.Title) ? $"{startSource} sedang berpikir..." : msg.Title;
                    StartAiWorking(startSource, startTitle);
                    break;

                case BridgeMessage.TypeComplete:
                case BridgeMessage.TypeGenerationCompleted:
                    var completeSource = string.IsNullOrWhiteSpace(msg.Source) ? "ChatGPT" : msg.Source;
                    var completeTitle = string.IsNullOrWhiteSpace(msg.Title) ? $"Jawaban {completeSource} sudah siap ✨" : msg.Title;
                    CompleteAiSuccess(completeSource, completeTitle);
                    break;

                case BridgeMessage.TypeCancel:
                case BridgeMessage.TypeGenerationCancelled:
                    var cancelSource = string.IsNullOrWhiteSpace(msg.Source) ? "AI" : msg.Source;
                    CancelAiWorking(cancelSource, $"Generasi {cancelSource} dihentikan.");
                    break;
            }
        });
    }

    private void StartAiWorking(string source, string message)
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "AI" : source;
        _activeAiSessions[resolvedSource] = (message, DateTime.Now);

        int activeCount = _activeAiSessions.Count;
        AIStatusBadgeText.Text = activeCount > 1 ? $"{activeCount} AI WORKING" : "WORKING";
        AIStatusBadge.Background = new SolidColorBrush(Color.FromRgb(245, 235, 255));
        AIStatusBadgeText.Foreground = (Brush)FindResource("PrimaryVioletBrush");

        string displayTag;
        string displayText;

        if (activeCount == 1)
        {
            displayTag = resolvedSource;
            displayText = message;
            AIStatusText.Text = message;
        }
        else
        {
            var names = string.Join(" + ", _activeAiSessions.Keys);
            displayTag = $"{activeCount} AI";
            displayText = $"[{names}] sedang berpikir...";
            AIStatusText.Text = $"Multi-AI Aktif: {names}";
        }

        if (AiWorkingTag != null) AiWorkingTag.Text = displayTag;
        if (AiWorkingText != null) AiWorkingText.Text = displayText;

        _stateManager.SetEmotion(PikoEmotion.Working, null, displayText);
        if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned)
        {
            TransitionToState(DynamicIslandState.AiWorking, (displayTag, displayText));
        }
        LogDiagnostic($"StartAiWorking: activeCount={activeCount}, source={resolvedSource}, message={message}");
    }

    private void CompleteAiSuccess(string source, string title)
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "AI" : source;
        _activeAiSessions.Remove(resolvedSource);

        _lastAiCompleteTime = DateTime.Now;
        _stateManager.TriggerNotification($"{resolvedSource} selesai! ✨", title, resolvedSource);

        _state.RecentAiEvents.Insert(0, new RecentAiEvent { Title = title, Source = resolvedSource, Timestamp = DateTime.Now });
        if (_state.RecentAiEvents.Count > 10) _state.RecentAiEvents.RemoveAt(_state.RecentAiEvents.Count - 1);
        SaveAppState();
        RenderRecentEvents();

        int remaining = _activeAiSessions.Count;
        if (remaining == 0)
        {
            AIStatusBadgeText.Text = "DONE ✨";
            AIStatusBadge.Background = new SolidColorBrush(Color.FromRgb(237, 229, 255));
            AIStatusBadgeText.Foreground = (Brush)FindResource("PrimaryVioletBrush");
            AIStatusText.Text = $"{resolvedSource} selesai! Semua AI standby.";
        }
        else
        {
            var names = string.Join(" + ", _activeAiSessions.Keys);
            AIStatusBadgeText.Text = $"{remaining} AI RUNNING";
            AIStatusText.Text = $"{resolvedSource} selesai! Masih aktif: {names}";
        }

        if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned)
        {
            TransitionToState(DynamicIslandState.Success, (resolvedSource, title));
        }
        LogDiagnostic($"CompleteAiSuccess: {resolvedSource} - {title}. Remaining AI: {remaining}");
    }

    private void CancelAiWorking(string source, string reason)
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "AI" : source;
        _activeAiSessions.Remove(resolvedSource);

        int remaining = _activeAiSessions.Count;
        if (remaining == 0)
        {
            AIStatusBadgeText.Text = "STOPPED";
            AIStatusText.Text = reason;
            _stateManager.SetEmotion(PikoEmotion.Alert, TimeSpan.FromSeconds(3), reason);
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Ai,
                Icon = "🛑",
                Title = $"{resolvedSource} Dibatalkan",
                Message = reason,
                Duration = TimeSpan.FromSeconds(3)
            });
        }
        else
        {
            var names = string.Join(" + ", _activeAiSessions.Keys);
            AIStatusBadgeText.Text = $"{remaining} AI RUNNING";
            AIStatusText.Text = $"{resolvedSource} dibatalkan. Masih aktif: {names}";
            if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned)
            {
                TransitionToState(DynamicIslandState.AiWorking, ($"{remaining} AI", $"[{names}] sedang berpikir..."));
            }
        }
        LogDiagnostic($"CancelAiWorking: {resolvedSource} - {reason}. Remaining AI: {remaining}");
    }

    private void BridgeService_StatusChanged(string status)
    {
        Dispatcher.Invoke(() => BridgeConnectionHint.Text = status);
    }

    private int _testAlertCounter = 0;

    private void TestAlert_Click(object sender, RoutedEventArgs e)
    {
        string[] providers = { "ChatGPT", "Gemini", "Claude", "DeepSeek", "Antigravity" };
        var prov = providers[_testAlertCounter % providers.Length];
        _testAlertCounter++;
        CompleteAiSuccess(prov, $"Jawaban {prov} sudah siap ✨");
    }

    private async void TestMultiAi_Click(object sender, RoutedEventArgs e)
    {
        // Simulates user's real concurrent workflow: ChatGPT and Gemini start together,
        // then Gemini finishes while ChatGPT is still running, then ChatGPT finishes!
        StartAiWorking("ChatGPT", "ChatGPT sedang menyusun kode...");
        await Task.Delay(1000);
        StartAiWorking("Gemini", "Gemini sedang menganalisis dokumen...");
        await Task.Delay(2500);
        CompleteAiSuccess("Gemini", "Jawaban Gemini sudah siap ✨");
        await Task.Delay(3000);
        CompleteAiSuccess("ChatGPT", "Kode ChatGPT sudah siap ✨");
    }

    private void RenderRecentEvents()
    {
        RecentAiEventsPanel.Children.Clear();
        if (_state.RecentAiEvents.Count == 0)
        {
            RecentAiEventsPanel.Children.Add(new TextBlock
            {
                Text = "Belum ada riwayat notifikasi baru.",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return;
        }

        foreach (var ev in _state.RecentAiEvents.Take(6))
        {
            var dock = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var timeBlock = new TextBlock
            {
                Text = ev.FormattedTime,
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(timeBlock, Dock.Right);
            dock.Children.Add(timeBlock);

            dock.Children.Add(new TextBlock
            {
                Text = $"• {ev.Source}: {ev.Title}",
                Foreground = (Brush)FindResource("MainTextBrush"),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            RecentAiEventsPanel.Children.Add(dock);
        }
    }

    private void ToggleSound_Click(object sender, RoutedEventArgs e)
    {
        _state.SoundEnabled = !_state.SoundEnabled;
        _soundService.SoundEnabled = _state.SoundEnabled;
        SoundToggleButton.Content = _state.SoundEnabled ? "♫ Sound On" : "♫ Sound Off";
        SettingsSoundBtn.Content = _state.SoundEnabled ? "Enabled" : "Muted";
        SaveAppState();
        _soundService.PlayGentleTap();
    }

    private void ToggleDnd_Click(object sender, RoutedEventArgs e)
    {
        _state.DoNotDisturb = !_state.DoNotDisturb;
        _soundService.DoNotDisturb = _state.DoNotDisturb;
        DndMiniButton.Content = _state.DoNotDisturb ? "🔕" : "🔔";
        DndToggleButton.Content = _state.DoNotDisturb ? "🔕 DND On" : "🔔 DND Off";
        SettingsDndBtn.Content = _state.DoNotDisturb ? "Active" : "Disabled";
        SaveAppState();
    }

    private void ToggleReducedMotion_Click(object sender, RoutedEventArgs e)
    {
        _state.ReducedMotion = !_state.ReducedMotion;
        SettingsReducedMotionBtn.Content = _state.ReducedMotion ? "On" : "Off";
        SaveAppState();
    }

    private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
    {
        _state.DarkMode = !_state.DarkMode;
        SettingsDarkModeBtn.Content = _state.DarkMode ? "On" : "Off";
        ApplyTheme();
        SaveAppState();
        _soundService.PlayGentleTap();
    }

    private Brush GetCurrentShellBackground() =>
        _state.DarkMode
            ? (Brush)FindResource("Dark_GlassShellBrush")
            : (Brush)FindResource("GlassShellBrush");

    private Brush GetCurrentShellBorderBrush() =>
        _state.DarkMode
            ? (Brush)FindResource("Dark_GlassBorderBrush")
            : (Brush)FindResource("GlassBorderBrush");

    /// <summary>
    /// Dynamically applies light or dark theme to the expanded dashboard panel.
    /// The Dynamic Island notch (IslandBar) always stays obsidian dark regardless of theme.
    /// </summary>
    private void ApplyTheme()
    {
        bool dark = _state.DarkMode;

        // --- Dashboard Shell (Expanded panel background) ---
        var shellBg = GetCurrentShellBackground();
        var shellBorder = GetCurrentShellBorderBrush();

        // Only apply to Shell when expanded, PositionNotch/TransitionToState handles Shell in collapsed states
        _originalShellBackground = shellBg;
        _originalShellBorderBrush = shellBorder;

        // --- Card backgrounds ---
        var cardBg = dark
            ? (Brush)FindResource("Dark_GlassCardBrush")
            : (Brush)FindResource("GlassCardBrush");
        var cardBorder = dark
            ? (Brush)FindResource("Dark_GlassBorderBrush")
            : (Brush)FindResource("GlassBorderBrush");

        // --- Text colors ---
        var textMain = dark
            ? (Brush)FindResource("Dark_TextPrimaryBrush")
            : (Brush)FindResource("MainTextBrush");
        var textMuted = dark
            ? (Brush)FindResource("Dark_TextSecondaryBrush")
            : (Brush)FindResource("MutedBrush");

        // --- Section label color ---
        var sectionColor = dark
            ? (Brush)FindResource("LavenderBrush")
            : (Brush)FindResource("DeepVioletBrush");

        // --- Chip/Badge backgrounds ---
        var chipBg = dark
            ? (Brush)FindResource("Dark_SoftChipBrush")
            : (Brush)FindResource("SoftChipBrush");
        var chipBorder = dark
            ? (Brush)FindResource("Dark_BorderBrush")
            : (Brush)FindResource("LightPurpleBrush");

        // --- Input backgrounds & dynamic resource brushes ---
        var inputBg = dark
            ? new SolidColorBrush(Color.FromArgb(0xEA, 0x1A, 0x16, 0x28))
            : new SolidColorBrush(Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF));
        var inputFg = dark
            ? Brushes.White
            : Brushes.Black;
        var inputBorder = dark
            ? (Brush)FindResource("Dark_BorderBrush")
            : (Brush)FindResource("LightPurpleBrush");
        var inputFocusedBg = dark
            ? new SolidColorBrush(Color.FromArgb(0xFA, 0x24, 0x1E, 0x38))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        var inputFocusedBorder = (Brush)FindResource("PrimaryVioletBrush");
        var inputPopupBg = dark
            ? new SolidColorBrush(Color.FromRgb(0x19, 0x15, 0x27))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        var inputCaret = dark
            ? Brushes.White
            : Brushes.Black;
        var inputWatermark = dark
            ? new SolidColorBrush(Color.FromArgb(0x95, 0xEE, 0xEB, 0xF8))
            : new SolidColorBrush(Color.FromArgb(0x90, 0x75, 0x6F, 0x93));

        // Sync Application-level and Window-level dynamic resource brushes
        if (Application.Current != null)
        {
            Application.Current.Resources["InputBackgroundBrush"] = inputBg;
            Application.Current.Resources["InputForegroundBrush"] = inputFg;
            Application.Current.Resources["InputBorderBrush"] = inputBorder;
            Application.Current.Resources["InputFocusedBackgroundBrush"] = inputFocusedBg;
            Application.Current.Resources["InputFocusedBorderBrush"] = inputFocusedBorder;
            Application.Current.Resources["InputPopupBackgroundBrush"] = inputPopupBg;
            Application.Current.Resources["InputCaretBrush"] = inputCaret;
            Application.Current.Resources["InputWatermarkBrush"] = inputWatermark;
        }

        Resources["InputBackgroundBrush"] = inputBg;
        Resources["InputForegroundBrush"] = inputFg;
        Resources["InputBorderBrush"] = inputBorder;
        Resources["InputFocusedBackgroundBrush"] = inputFocusedBg;
        Resources["InputFocusedBorderBrush"] = inputFocusedBorder;
        Resources["InputPopupBackgroundBrush"] = inputPopupBg;
        Resources["InputCaretBrush"] = inputCaret;
        Resources["InputWatermarkBrush"] = inputWatermark;

        // --- Tab nav header ---
        var tabHeaderBg = dark
            ? new SolidColorBrush(Color.FromArgb(0x90, 0x18, 0x14, 0x25))
            : new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

        // --- Progress bar track ---
        var progressTrack = dark
            ? new SolidColorBrush(Color.FromRgb(0x2D, 0x26, 0x45))
            : new SolidColorBrush(Color.FromRgb(0xE4, 0xDE, 0xFF));

        // --- About / info panel background ---
        var aboutBg = dark
            ? new SolidColorBrush(Color.FromRgb(0x18, 0x14, 0x25))
            : new SolidColorBrush(Color.FromRgb(0xF6, 0xF3, 0xFF));

        // Refresh Shell if currently expanded
        if (_currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned)
        {
            Shell.Background = shellBg;
            Shell.BorderBrush = shellBorder;
        }

        // Brand text
        BrandText.Foreground = textMain;

        // Header controls text
        MiniStatusText.Foreground = dark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("DeepVioletBrush");
        ClockText.Foreground = textMain;

        // Apply direct to known input controls across tabs
        ApplyInputThemeDirect(dark, inputBg, inputFg, inputBorder, inputCaret);

        // Apply to all expanded content panels recursively
        ApplyThemeToPanel(ExpandedContent, textMain, textMuted, sectionColor, cardBg, cardBorder, chipBg, chipBorder, inputBg, inputBorder, tabHeaderBg, progressTrack, aboutBg, dark);

        EnsureActiveTabValid();
        RefreshStorageAnalysisUI();
        UpdateShelfUI();
        UpdateTodoList();

        if (CalendarGridFrame != null)
        {
            CalendarGridFrame.Background = dark
                ? new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x24))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xF9, 0xFE));
            CalendarGridFrame.BorderBrush = dark
                ? new SolidColorBrush(Color.FromRgb(0x3D, 0x2D, 0x64))
                : new SolidColorBrush(Color.FromRgb(0xDC, 0xD0, 0xFF));
        }

        RenderCalendarUI();

        // Update the footer
        if (DashboardFooterText != null)
        {
            DashboardFooterText.Foreground = textMuted;
            DashboardFooterText.Text = dark
                ? "Piko · Your Tiny Desk Buddy · Obsidian & Lavender 3D Glass"
                : "Piko · Your Tiny Desk Buddy · White & Purple 3D Glass";
        }

        LogDiagnostic($"ApplyTheme: dark={dark}");
    }

    private void ApplyInputThemeDirect(bool dark, Brush bg, Brush fg, Brush border, Brush caret)
    {
        if (TodoInput != null)
        {
            TodoInput.Background = bg;
            TodoInput.Foreground = fg;
            TodoInput.BorderBrush = border;
            TodoInput.CaretBrush = caret;
            TextElement.SetForeground(TodoInput, fg);
        }

        if (DueCombo != null)
        {
            DueCombo.Background = bg;
            DueCombo.Foreground = fg;
            DueCombo.BorderBrush = border;
            TextElement.SetForeground(DueCombo, fg);
            foreach (var item in DueCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    cbi.Foreground = fg;
                    TextElement.SetForeground(cbi, fg);
                }
            }
        }

        if (TargetFolderInput != null)
        {
            TargetFolderInput.Background = bg;
            TargetFolderInput.Foreground = fg;
            TargetFolderInput.BorderBrush = border;
            TargetFolderInput.CaretBrush = caret;
            TextElement.SetForeground(TargetFolderInput, fg);
        }

        if (StorageFolderCombo != null)
        {
            StorageFolderCombo.Background = bg;
            StorageFolderCombo.Foreground = fg;
            StorageFolderCombo.BorderBrush = border;
            TextElement.SetForeground(StorageFolderCombo, fg);
            foreach (var item in StorageFolderCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    cbi.Foreground = fg;
                    TextElement.SetForeground(cbi, fg);
                }
            }
        }

        if (GoogleClientIdInput != null)
        {
            GoogleClientIdInput.Background = bg;
            GoogleClientIdInput.Foreground = fg;
            GoogleClientIdInput.BorderBrush = border;
            GoogleClientIdInput.CaretBrush = caret;
            TextElement.SetForeground(GoogleClientIdInput, fg);
        }

        if (GoogleClientSecretInput != null)
        {
            GoogleClientSecretInput.Background = bg;
            GoogleClientSecretInput.Foreground = fg;
            GoogleClientSecretInput.BorderBrush = border;
            GoogleClientSecretInput.CaretBrush = caret;
            TextElement.SetForeground(GoogleClientSecretInput, fg);
        }

        if (IcalAccountEmailInput != null)
        {
            IcalAccountEmailInput.Background = bg;
            IcalAccountEmailInput.Foreground = fg;
            IcalAccountEmailInput.BorderBrush = border;
            IcalAccountEmailInput.CaretBrush = caret;
            TextElement.SetForeground(IcalAccountEmailInput, fg);
        }

        if (IcalUrlInput != null)
        {
            IcalUrlInput.Background = bg;
            IcalUrlInput.Foreground = fg;
            IcalUrlInput.BorderBrush = border;
            IcalUrlInput.CaretBrush = caret;
            TextElement.SetForeground(IcalUrlInput, fg);
        }

        if (OAuthClientIdInput != null)
        {
            OAuthClientIdInput.Background = bg;
            OAuthClientIdInput.Foreground = fg;
            OAuthClientIdInput.BorderBrush = border;
            OAuthClientIdInput.CaretBrush = caret;
            TextElement.SetForeground(OAuthClientIdInput, fg);
        }

        if (OAuthClientSecretInput != null)
        {
            OAuthClientSecretInput.Background = bg;
            OAuthClientSecretInput.Foreground = fg;
            OAuthClientSecretInput.BorderBrush = border;
            OAuthClientSecretInput.CaretBrush = caret;
            TextElement.SetForeground(OAuthClientSecretInput, fg);
        }

        if (PomodoroTaskCombo != null)
        {
            PomodoroTaskCombo.Background = bg;
            PomodoroTaskCombo.Foreground = fg;
            PomodoroTaskCombo.BorderBrush = border;
            TextElement.SetForeground(PomodoroTaskCombo, fg);
        }

        if (PomodoroLongBreakCombo != null)
        {
            PomodoroLongBreakCombo.Background = bg;
            PomodoroLongBreakCombo.Foreground = fg;
            PomodoroLongBreakCombo.BorderBrush = border;
            TextElement.SetForeground(PomodoroLongBreakCombo, fg);
            foreach (var item in PomodoroLongBreakCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    cbi.Foreground = fg;
                    TextElement.SetForeground(cbi, fg);
                }
            }
        }
    }

    /// <summary>
    /// Recursively applies theme colors to the expanded dashboard panels.
    /// Targets specific named elements and card Borders for correct theming.
    /// </summary>
    private void ApplyThemeToPanel(
        FrameworkElement root, Brush textMain, Brush textMuted, Brush sectionColor,
        Brush cardBg, Brush cardBorder, Brush chipBg, Brush chipBorder,
        Brush inputBg, Brush inputBorder, Brush tabHeaderBg, Brush progressTrack, Brush aboutBg,
        bool dark)
    {
        ApplyThemeRecursive(root, textMain, textMuted, sectionColor, cardBg, cardBorder,
            chipBg, chipBorder, inputBg, inputBorder, tabHeaderBg, progressTrack, aboutBg, dark, depth: 0);
    }

    private void ApplyThemeRecursive(
        DependencyObject obj, Brush textMain, Brush textMuted, Brush sectionColor,
        Brush cardBg, Brush cardBorder, Brush chipBg, Brush chipBorder,
        Brush inputBg, Brush inputBorder, Brush tabHeaderBg, Brush progressTrack, Brush aboutBg,
        bool dark, int depth)
    {
        if (depth > 30 || obj == null) return;

        int childCount = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);

            if (child is TextBlock tb)
            {
                var currentBrush = tb.Foreground;
                // MainText-colored TextBlocks
                if (IsMainTextBrush(currentBrush, dark))
                    tb.Foreground = textMain;
                // Muted-colored TextBlocks
                else if (IsMutedBrush(currentBrush, dark))
                    tb.Foreground = textMuted;
                // Section labels (DeepViolet / Lavender)
                else if (IsSectionBrush(currentBrush, dark))
                    tb.Foreground = sectionColor;
            }
            else if (child is Border border)
            {
                // Template borders must NEVER be overwritten by chip/card logic
                if (border.TemplatedParent != null || border.Name == "TabBorder")
                {
                    continue;
                }

                // Card-style borders (using CardStyle)
                var style = border.Style;
                if (style != null && style == FindResource("CardStyle") as Style)
                {
                    border.Background = cardBg;
                    border.BorderBrush = cardBorder;
                    if (dark)
                    {
                        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = 14, ShadowDepth = 2, Direction = 270,
                            Color = Color.FromRgb(0x0F, 0x0D, 0x18), Opacity = 0.3
                        };
                    }
                    else
                    {
                        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            BlurRadius = 14, ShadowDepth = 2, Direction = 270,
                            Color = Color.FromRgb(0x5B, 0x3A, 0xA8), Opacity = 0.07
                        };
                    }
                }

                // Tab navigation header
                if (border.Parent is Grid parentGrid && parentGrid == ExpandedContent)
                {
                    if (border.CornerRadius.TopLeft >= 14 && border.Padding.Left >= 3)
                    {
                        border.Background = tabHeaderBg;
                        border.BorderBrush = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");
                    }
                }

                // Chip badges, cards, and categories breakdown / duplicate cards
                if ((border.CornerRadius.TopLeft >= 6 && border.CornerRadius.TopLeft <= 16)
                    || border.Tag is "CategoryChipCard" or "DuplicateGroupCard")
                {
                    if (border.Background is SolidColorBrush scb)
                    {
                        var c = scb.Color;
                        // Light chip bg range or dark chip bg range
                        if ((c.R >= 0xD0 && c.G >= 0xC0 && c.B >= 0xE0) || (c.R <= 0x35 && c.G <= 0x2A && c.B <= 0x48))
                        {
                            border.Background = chipBg;
                            border.BorderBrush = chipBorder;
                        }
                    }
                    else if (border.Background is LinearGradientBrush)
                    {
                        border.Background = chipBg;
                        border.BorderBrush = chipBorder;
                    }
                }

                // About/info panels (#F6F3FF / #F7F4FF)
                if (border.CornerRadius.TopLeft >= 10 && border.Padding.Left >= 10)
                {
                    if (border.Background is SolidColorBrush infoScb)
                    {
                        var c = infoScb.Color;
                        if ((c.R >= 0xF4 && c.G >= 0xF0 && c.B >= 0xF8) || (c.R <= 0x1E && c.G <= 0x18 && c.B <= 0x2A))
                        {
                            border.Background = aboutBg;
                            border.BorderBrush = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");
                        }
                    }
                }
            }
            else if (child is ProgressBar pb)
            {
                pb.Background = progressTrack;
                continue;
            }
            else if (child is TextBox txb)
            {
                var inputFgDirect = dark ? Brushes.White : Brushes.Black;
                txb.Background = inputBg;
                txb.BorderBrush = inputBorder;
                txb.Foreground = inputFgDirect;
                txb.CaretBrush = inputFgDirect;
                TextElement.SetForeground(txb, inputFgDirect);
                continue;
            }
            else if (child is ComboBox cb)
            {
                var inputFgDirect = dark ? Brushes.White : Brushes.Black;
                cb.Background = inputBg;
                cb.Foreground = inputFgDirect;
                cb.BorderBrush = inputBorder;
                TextElement.SetForeground(cb, inputFgDirect);

                foreach (var item in cb.Items)
                {
                    if (item is ComboBoxItem cbi)
                    {
                        cbi.Foreground = inputFgDirect;
                        TextElement.SetForeground(cbi, inputFgDirect);
                    }
                }
                continue;
            }
            else if (child is RadioButton rb)
            {
                if (rb.Style == FindResource("TabNavButton") as Style)
                {
                    rb.Foreground = rb.IsChecked == true ? Brushes.White : textMuted;
                    continue;
                }

                if (rb.IsChecked != true)
                {
                    rb.Foreground = textMuted;
                }
            }
            else if (child is CheckBox chk)
            {
                chk.Foreground = textMain;
            }
            else if (child is Button btn)
            {
                if (btn.Style == FindResource("PikoButton") as Style)
                {
                    btn.Foreground = textMain;
                    btn.Background = chipBg;
                    btn.BorderBrush = chipBorder;
                }
            }

            ApplyThemeRecursive(child, textMain, textMuted, sectionColor, cardBg, cardBorder,
                chipBg, chipBorder, inputBg, inputBorder, tabHeaderBg, progressTrack, aboutBg, dark, depth + 1);
        }
    }

    // Brush matching helpers for theme switching
    private bool IsMainTextBrush(Brush brush, bool dark)
    {
        if (brush is not SolidColorBrush scb) return false;
        var c = scb.Color;
        // Light mode main text: #242044
        if (c.R == 0x24 && c.G == 0x20 && c.B == 0x44) return true;
        // Dark mode main text: #E8E4F0
        if (c.R == 0xE8 && c.G == 0xE4 && c.B == 0xF0) return true;
        return false;
    }

    private bool IsMutedBrush(Brush brush, bool dark)
    {
        if (brush is not SolidColorBrush scb) return false;
        var c = scb.Color;
        // Light mode muted: #756F93
        if (c.R == 0x75 && c.G == 0x6F && c.B == 0x93) return true;
        // Dark mode muted: #9B93B0
        if (c.R == 0x9B && c.G == 0x93 && c.B == 0xB0) return true;
        return false;
    }

    private bool IsSectionBrush(Brush brush, bool dark)
    {
        if (brush is not SolidColorBrush scb) return false;
        var c = scb.Color;
        // Light mode section: #5B3AA8 (DeepViolet)
        if (c.R == 0x5B && c.G == 0x3A && c.B == 0xA8) return true;
        // Dark mode section: #BBA2FF (Lavender)
        if (c.R == 0xBB && c.G == 0xA2 && c.B == 0xFF) return true;
        return false;
    }

    private void DismissNotification_Click(object sender, RoutedEventArgs e)
    {
        _stateManager.DismissNotification();
    }

    // ---------------------------------------------------------
    // AI STORAGE ASSISTANT (Scan, Overview, Duplicate, Smart Organize)
    // ---------------------------------------------------------

    private string? _customStoragePath;

    private void UpdateDriveStatsUI(string? currentPath = null)
    {
        var targetPath = currentPath ?? GetSelectedFolderPath();
        var root = Path.GetPathRoot(targetPath) ?? "C:\\";
        var (total, free, used) = StorageScannerService.GetDriveStats(root);
        if (total > 0)
        {
            var freeGb = free / (1024.0 * 1024.0 * 1024.0);
            var totalGb = total / (1024.0 * 1024.0 * 1024.0);
            var percent = (double)used / total * 100.0;
            var driveName = root.TrimEnd('\\');

            DriveStatsText.Text = $"Drive {driveName} {freeGb:F1} GB free of {totalGb:F1} GB";
            DriveProgressBar.Value = percent;
        }
    }

    private string GetSelectedFolderPath()
    {
        if (!string.IsNullOrEmpty(_customStoragePath) && Directory.Exists(_customStoragePath))
        {
            return _customStoragePath;
        }

        if (StorageFolderCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag switch
            {
                "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "D_Root" => Directory.Exists("D:\\") ? "D:\\" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private void StorageFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (StorageFolderCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag != _customStoragePath)
        {
            if (tag is "Downloads" or "Desktop" or "Documents" or "Pictures" or "Videos" or "D_Root")
            {
                _customStoragePath = null;
            }
        }
        var path = GetSelectedFolderPath();
        ScanStatusText.Text = $"Folder siap: {path}";
        UpdateDriveStatsUI(path);
    }

    private void BrowseStorageFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pilih Folder untuk AI Storage Assistant",
            InitialDirectory = Directory.Exists("D:\\") ? "D:\\" : "C:\\"
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName))
        {
            _customStoragePath = dialog.FolderName;

            var existing = StorageFolderCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string?)i.Tag == dialog.FolderName);
            if (existing == null)
            {
                var newItem = new ComboBoxItem
                {
                    Content = $"📁 {dialog.FolderName}",
                    Tag = dialog.FolderName,
                    Foreground = _state.DarkMode ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush")
                };
                StorageFolderCombo.Items.Add(newItem);
                StorageFolderCombo.SelectedItem = newItem;
            }
            else
            {
                StorageFolderCombo.SelectedItem = existing;
            }

            ScanStatusText.Text = $"Folder dipilih: {dialog.FolderName}";
            UpdateDriveStatsUI(dialog.FolderName);
        }
    }

    private void RefreshStorageAnalysisUI()
    {
        var dark = _state.DarkMode;
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");
        var sectionColor = dark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("DeepVioletBrush");
        var inputBg = dark ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x2E)) : new SolidColorBrush(Colors.White);
        var inputBorder = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");

        var inputFg = dark ? Brushes.White : Brushes.Black;
        if (StorageFolderCombo != null)
        {
            StorageFolderCombo.Background = inputBg;
            StorageFolderCombo.Foreground = inputFg;
            StorageFolderCombo.BorderBrush = inputBorder;

            foreach (var item in StorageFolderCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    cbi.Foreground = inputFg;
                }
            }
        }

        if (OrganizePlanSummary != null)
        {
            OrganizePlanSummary.Foreground = textMain;
        }

        if (ScanStatusText != null)
        {
            ScanStatusText.Foreground = textMuted;
        }

        if (DriveStatsText != null)
        {
            DriveStatsText.Foreground = sectionColor;
        }

        if (_lastScanResult != null)
        {
            RenderCategoryBreakdown(_lastScanResult);
            RenderLargeFiles(_lastScanResult);
            RenderDuplicates(_lastScanResult);
        }

        if (_currentPlan != null && _currentPlan.Count > 0)
        {
            RenderOrganizePlanItems();
        }

        RenderCustomTargetFolders();
    }

    private void RenderCustomTargetFolders()
    {
        if (TargetFoldersWrapPanel == null) return;
        TargetFoldersWrapPanel.Children.Clear();

        _state.CustomTargetFolders ??= new() { "Tugas", "Kuliah", "Dokumen" };

        var dark = _state.DarkMode;
        var chipBg = dark ? (Brush)FindResource("Dark_SoftChipBrush") : (Brush)FindResource("SoftChipBrush");
        var chipBorder = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        foreach (var folder in _state.CustomTargetFolders.ToList())
        {
            var folderCapture = folder;
            var displayName = Path.GetFileName(folderCapture.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(displayName)) displayName = folderCapture;

            var border = new Border
            {
                Background = chipBg,
                BorderBrush = chipBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8, 4, 6, 4),
                Margin = new Thickness(0, 0, 6, 6)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = $"📁 {displayName}",
                ToolTip = folderCapture,
                Foreground = textMain,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeBtn = new Button
            {
                Content = "✕",
                ToolTip = "Hapus folder aturan ini",
                Foreground = textMuted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand
            };
            removeBtn.Click += async (_, _) =>
            {
                _state.CustomTargetFolders.Remove(folderCapture);
                SaveAppState();
                RenderCustomTargetFolders();
                if (_lastScanResult != null)
                {
                    await GenerateOrganizePlanAsync(_lastScanResult);
                }
            };
            sp.Children.Add(removeBtn);

            border.Child = sp;
            TargetFoldersWrapPanel.Children.Add(border);
        }
    }

    private async void AddTargetFolder_Click(object sender, RoutedEventArgs e)
    {
        var input = TargetFolderInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        _state.CustomTargetFolders ??= new();
        if (!_state.CustomTargetFolders.Any(f => string.Equals(f, input, StringComparison.OrdinalIgnoreCase)))
        {
            _state.CustomTargetFolders.Add(input);
            SaveAppState();
            TargetFolderInput.Clear();
            RenderCustomTargetFolders();

            if (_lastScanResult != null)
            {
                await GenerateOrganizePlanAsync(_lastScanResult);
            }
        }
    }

    private void TargetFolderInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTargetFolder_Click(sender, e);
        }
    }

    private async void BrowseTargetFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pilih Folder Target Disk",
            InitialDirectory = Directory.Exists("D:\\") ? "D:\\" : "C:\\"
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName))
        {
            _state.CustomTargetFolders ??= new();
            if (!_state.CustomTargetFolders.Any(f => string.Equals(f, dialog.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                _state.CustomTargetFolders.Add(dialog.FolderName);
                SaveAppState();
                RenderCustomTargetFolders();

                if (_lastScanResult != null)
                {
                    await GenerateOrganizePlanAsync(_lastScanResult);
                }
            }
        }
    }

    private async void ScanStorage_Click(object sender, RoutedEventArgs e)
    {
        var targetPath = GetSelectedFolderPath();
        if (!Directory.Exists(targetPath))
        {
            ScanStatusText.Text = $"Folder tidak ditemukan: {targetPath}";
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        _stateManager.SetEmotion(PikoEmotion.Working, null, "Memindai penyimpanan...");
        ScanStatusText.Text = $"Sedang memindai {Path.GetFileName(targetPath)}...";

        var progress = new Progress<int>(count =>
        {
            ScanStatusText.Text = $"Memindai... {count} file ditemukan";
        });

        try
        {
            var result = await _storageScanner.ScanFolderAsync(targetPath, false, progress, _scanCts.Token);
            _lastScanResult = result;

            RenderCategoryBreakdown(result);
            RenderLargeFiles(result);
            RenderDuplicates(result);
            await GenerateOrganizePlanAsync(result);

            ScanStatusText.Text = $"Selesai! {result.TotalFiles} file ({result.FormattedTotalSize}) dianalisis.";
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(5), "Penyimpanan berhasil dipindai!");
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "Pemindaian dibatalkan.";
            _stateManager.SetEmotion(PikoEmotion.Idle);
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Error: {ex.Message}";
            _stateManager.SetEmotion(PikoEmotion.Error, TimeSpan.FromSeconds(4));
        }
    }

    private void RenderCategoryBreakdown(StorageScanResult result)
    {
        CategoriesBorder.Visibility = Visibility.Visible;
        CategoriesWrapPanel.Children.Clear();

        var dark = _state.DarkMode;
        var chipBg = dark ? (Brush)FindResource("Dark_SoftChipBrush") : (Brush)FindResource("SoftChipBrush");
        var chipBorder = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        foreach (var cat in result.Categories.Where(c => c.FileCount > 0))
        {
            var card = new Border
            {
                Background = chipBg,
                BorderBrush = chipBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = "CategoryChipCard"
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{cat.Icon} {cat.Name}",
                Foreground = textMain,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{cat.FileCount} file · {cat.FormattedSize}",
                Foreground = textMuted,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0)
            });

            card.Child = sp;
            CategoriesWrapPanel.Children.Add(card);
        }
    }

    private void RenderLargeFiles(StorageScanResult result)
    {
        LargeFilesBorder.Visibility = result.LargeFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LargeFilesCountText.Text = $"{result.LargeFiles.Count} file";
        LargeFilesPanel.Children.Clear();

        var dark = _state.DarkMode;
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");
        LargeFilesCountText.Foreground = textMuted;

        foreach (var file in result.LargeFiles)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var sizeBlock = new TextBlock
            {
                Text = file.FormattedSize,
                Foreground = (Brush)FindResource("PrimaryVioletBrush"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(sizeBlock, Dock.Right);
            dock.Children.Add(sizeBlock);

            dock.Children.Add(new TextBlock
            {
                Text = file.FileName,
                ToolTip = file.FilePath,
                Foreground = textMain,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            LargeFilesPanel.Children.Add(dock);
        }
    }

    private void RenderDuplicates(StorageScanResult result)
    {
        DuplicatesBorder.Visibility = result.DuplicateGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DuplicatesCountText.Text = $"{result.DuplicateGroups.Count} grup";
        DuplicatesPanel.Children.Clear();

        var dark = _state.DarkMode;
        var chipBg = dark ? (Brush)FindResource("Dark_SoftChipBrush") : (Brush)FindResource("SoftChipBrush");
        var chipBorder = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");
        var textHeader = dark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("DeepVioletBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");
        DuplicatesCountText.Foreground = textMuted;

        foreach (var group in result.DuplicateGroups)
        {
            var groupBorder = new Border
            {
                Background = chipBg,
                BorderBrush = chipBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = "DuplicateGroupCard"
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"Identik ({group.Files.Count} file · {group.FormattedSize} masing-masing):",
                Foreground = textHeader,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });

            foreach (var f in group.Files)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"• {f.FileName}",
                    ToolTip = f.FilePath,
                    Foreground = textMuted,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 1, 0, 0)
                });
            }

            groupBorder.Child = sp;
            DuplicatesPanel.Children.Add(groupBorder);
        }
    }

    private async Task GenerateOrganizePlanAsync(StorageScanResult result)
    {
        _state.CustomTargetFolders ??= new() { "Tugas", "Kuliah", "Dokumen" };
        _currentPlan = await _storageOrganizer.GeneratePlanAsync(result, _state.CustomTargetFolders);

        if (_currentPlan.Count == 0)
        {
            SmartOrganizeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        SmartOrganizeBorder.Visibility = Visibility.Visible;
        OrganizePlanSummary.Text = $"Usulan Penataan: {_currentPlan.Count} file dapat dirapikan";
        RenderOrganizePlanItems();
    }

    private void RenderOrganizePlanItems()
    {
        if (PlanItemsPanel == null || _currentPlan == null) return;
        PlanItemsPanel.Children.Clear();

        var dark = _state.DarkMode;
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");
        var statusPending = dark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("DeepVioletBrush");
        var rowBg = dark ? new SolidColorBrush(Color.FromArgb(0x28, 0x22, 0x1D, 0x33)) : new SolidColorBrush(Color.FromArgb(0x35, 0xF5, 0xF0, 0xFF));

        foreach (var item in _currentPlan)
        {
            var rowBorder = new Border
            {
                Background = rowBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 2, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = item.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            cb.Checked += (_, _) => item.IsSelected = true;
            cb.Unchecked += (_, _) => item.IsSelected = false;
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = item.FileName,
                ToolTip = item.SourcePath,
                Foreground = textMain,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var targetDirName = Path.GetFileName(item.TargetFolder);
            if (string.IsNullOrEmpty(targetDirName)) targetDirName = item.TargetFolder;
            sp.Children.Add(new TextBlock
            {
                Text = $"➔ {targetDirName} ({item.FormattedSize}) · {item.Reason}",
                Foreground = textMuted,
                FontSize = 9.5
            });
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            var statusBlock = new TextBlock
            {
                Text = item.Status == PlanItemStatus.Moved ? "✓ Selesai" : (item.Status == PlanItemStatus.Failed ? "⚠ Gagal" : "Menunggu"),
                Foreground = item.Status == PlanItemStatus.Moved ? (Brush)FindResource("SuccessGreenBrush") : statusPending,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0)
            };
            Grid.SetColumn(statusBlock, 2);
            grid.Children.Add(statusBlock);

            rowBorder.Child = grid;
            PlanItemsPanel.Children.Add(rowBorder);
        }
    }

    private async void ApproveMove_Click(object sender, RoutedEventArgs e)
    {
        var selected = _currentPlan.Where(i => i.IsSelected && i.Status == PlanItemStatus.Pending).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Tidak ada file yang dipilih untuk dirapikan.", "Piko Storage Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApproveMoveButton.IsEnabled = false;
        _stateManager.SetEmotion(PikoEmotion.Working, null, "Memindahkan file secara aman...");

        try
        {
            var journal = await _storageOrganizer.ExecutePlanAsync(_currentPlan, null);
            _state.OperationJournals.Add(journal);
            SaveAppState();

            UndoMoveButton.IsEnabled = true;
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(6), $"Berhasil merapikan {journal.Entries.Count} file!");
            ScanStatusText.Text = $"Selesai! {journal.Entries.Count} file berhasil dipindahkan ke folder rapi. Bisa di-Undo kapan saja.";

            if (_lastScanResult != null)
            {
                await GenerateOrganizePlanAsync(_lastScanResult);
            }
        }
        catch (Exception ex)
        {
            _stateManager.SetEmotion(PikoEmotion.Error, TimeSpan.FromSeconds(4));
            ScanStatusText.Text = $"Error saat memindahkan file: {ex.Message}";
        }
        finally
        {
            ApproveMoveButton.IsEnabled = true;
        }
    }

    private async void UndoMove_Click(object sender, RoutedEventArgs e)
    {
        if (_state.OperationJournals.Count == 0) return;

        var lastJournal = _state.OperationJournals.Last();
        UndoMoveButton.IsEnabled = false;
        _stateManager.SetEmotion(PikoEmotion.Working, null, "Mengembalikan file...");

        try
        {
            var (reverted, failed) = await _storageOrganizer.UndoJournalAsync(lastJournal);
            _state.OperationJournals.Remove(lastJournal);
            SaveAppState();

            UndoMoveButton.IsEnabled = _state.OperationJournals.Count > 0;
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(5), "Operasi berhasil dibatalkan!");
            ScanStatusText.Text = $"Undo selesai: {reverted} file dikembalikan ke lokasi semula. ({failed} gagal/hilang)";
        }
        catch (Exception ex)
        {
            _stateManager.SetEmotion(PikoEmotion.Error, TimeSpan.FromSeconds(4));
            ScanStatusText.Text = $"Error saat Undo: {ex.Message}";
        }
    }

    // ---------------------------------------------------------
    // 9 BALANCED TABS NAVIGATION
    // ---------------------------------------------------------

    private StackPanel? _currentActivePanel;

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateTabPanels();
        _soundService.PlayGentleTap();
    }

    private void UpdateTabPanels()
    {
        StackPanel target = PanelHome;
        if (TabMusic.IsChecked == true) target = PanelMusic;
        else if (TabFocus.IsChecked == true) target = PanelFocus;
        else if (TabWater.IsChecked == true) target = PanelWater;
        else if (TabTasks.IsChecked == true) target = PanelTasks;
        else if (TabShelf.IsChecked == true) target = PanelShelf;
        else if (TabStorage.IsChecked == true) target = PanelStorage;
        else if (TabCalendar.IsChecked == true) target = PanelCalendar;
        else if (TabSettings.IsChecked == true) target = PanelSettings;

        SwitchToTabPanel(target);
    }

    private void SwitchToTabPanel(StackPanel newPanel)
    {
        var allPanels = new[] { PanelHome, PanelMusic, PanelFocus, PanelWater, PanelTasks, PanelShelf, PanelStorage, PanelCalendar, PanelSettings };
        var allTabs = new[] { TabHome, TabMusic, TabFocus, TabWater, TabTasks, TabShelf, TabStorage, TabCalendar, TabSettings };

        int targetIdx = Array.IndexOf(allPanels, newPanel);
        if (targetIdx >= 0 && allTabs[targetIdx].IsChecked != true)
        {
            allTabs[targetIdx].IsChecked = true;
        }

        if (newPanel == _currentActivePanel && newPanel.Visibility == Visibility.Visible) return;

        foreach (var p in allPanels)
        {
            if (p != newPanel)
            {
                p.Visibility = Visibility.Collapsed;
                p.BeginAnimation(OpacityProperty, null);
            }
        }

        _currentActivePanel = newPanel;
        newPanel.Visibility = Visibility.Visible;
        if (newPanel == PanelCalendar)
        {
            RenderCalendarUI();
        }
        ApplyTheme();

        if (_state.ReducedMotion)
        {
            newPanel.Opacity = 1.0;
            return;
        }

        newPanel.Opacity = 0.0;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        newPanel.BeginAnimation(OpacityProperty, fadeIn);

        var translate = new TranslateTransform(0, 8);
        newPanel.RenderTransform = translate;
        var slideUp = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void EnsureActiveTabValid()
    {
        var allTabs = new[] { TabHome, TabMusic, TabFocus, TabWater, TabTasks, TabShelf, TabStorage, TabCalendar, TabSettings };
        var allPanels = new[] { PanelHome, PanelMusic, PanelFocus, PanelWater, PanelTasks, PanelShelf, PanelStorage, PanelCalendar, PanelSettings };

        RadioButton? activeTab = allTabs.FirstOrDefault(t => t.IsChecked == true);
        if (activeTab == null)
        {
            if (_currentActivePanel != null)
            {
                int pIdx = Array.IndexOf(allPanels, _currentActivePanel);
                if (pIdx >= 0 && pIdx < allTabs.Length)
                {
                    allTabs[pIdx].IsChecked = true;
                    activeTab = allTabs[pIdx];
                }
            }

            if (activeTab == null)
            {
                TabHome.IsChecked = true;
                activeTab = TabHome;
            }
        }

        int idx = Array.IndexOf(allTabs, activeTab);
        if (idx >= 0 && idx < allPanels.Length)
        {
            var targetPanel = allPanels[idx];
            if (_currentActivePanel != targetPanel || targetPanel.Visibility != Visibility.Visible)
            {
                _currentActivePanel = targetPanel;
                foreach (var p in allPanels)
                {
                    p.Visibility = p == targetPanel ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void HomeFocusJump_Click(object sender, RoutedEventArgs e)
    {
        TabFocus.IsChecked = true;
    }

    private void HomeTasksJump_Click(object sender, RoutedEventArgs e)
    {
        TabTasks.IsChecked = true;
    }

    // ---------------------------------------------------------
    // MUSIC DEMO
    // ---------------------------------------------------------

    private void UpdateTrackDisplay()
    {
        if (_sharedMedia.HasMedia && _sharedMedia.Source != "Demo")
        {
            SyncSharedMediaUI();
            return;
        }

        if (_demoPlaying)
        {
            var track = _demoTracks[_trackIndex];
            _sharedMedia.Source = "Demo";
            _sharedMedia.Title = track.Title;
            _sharedMedia.Channel = track.Artist;
            _sharedMedia.PlaybackState = "playing";
            SyncSharedMediaUI();
        }
        else
        {
            SyncSharedMediaUI();
        }
    }

    private void PreviousTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_sharedMedia.HasMedia && _sharedMedia.Source != "Demo")
        {
            _commandTimeoutTimer.Stop();
            _commandTimeoutTimer.Start();
            _activeCommand = _bridgeService.EnqueueMediaCommand("prev", _sharedMedia.TabId);
            MediaStatusText.Text = "Mengirim perintah Previous ke YouTube... (menunggu konfirmasi)";
            return;
        }

        if (_demoPlaying)
        {
            _trackIndex = (_trackIndex - 1 + _demoTracks.Length) % _demoTracks.Length;
            UpdateTrackDisplay();
            MediaStatusText.Text = "Preview track demo berganti.";
            ShowMusicActivity();
            return;
        }

        MediaStatusText.Text = "Tidak ada media YouTube aktif untuk memutar video sebelumnya.";
    }

    private void NextTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_sharedMedia.HasMedia && _sharedMedia.Source != "Demo")
        {
            _commandTimeoutTimer.Stop();
            _commandTimeoutTimer.Start();
            _activeCommand = _bridgeService.EnqueueMediaCommand("next", _sharedMedia.TabId);
            MediaStatusText.Text = "Mengirim perintah Next ke YouTube... (menunggu konfirmasi)";
            return;
        }

        if (_demoPlaying)
        {
            _trackIndex = (_trackIndex + 1) % _demoTracks.Length;
            UpdateTrackDisplay();
            MediaStatusText.Text = "Preview track demo berganti.";
            ShowMusicActivity();
            return;
        }

        MediaStatusText.Text = "Tidak ada media YouTube aktif untuk berpindah ke video berikutnya.";
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sharedMedia.HasMedia && _sharedMedia.Source != "Demo")
        {
            var nextAction = _sharedMedia.PlaybackState == "playing" ? "pause" : "play";
            _commandTimeoutTimer.Stop();
            _commandTimeoutTimer.Start();
            _activeCommand = _bridgeService.EnqueueMediaCommand(nextAction, _sharedMedia.TabId);
            MediaStatusText.Text = nextAction == "pause"
                ? "Mengirim perintah Pause ke YouTube... (menunggu konfirmasi)"
                : "Mengirim perintah Play ke YouTube... (menunggu konfirmasi)";
            return;
        }

        if (_demoPlaying)
        {
            _sharedMedia.Reset();
            SyncSharedMediaUI();
            StopMusicActivity();
            _stateManager.SetEmotion(PikoEmotion.Idle);
            MediaStatusText.Text = "Mode Demo visual dinonaktifkan.";
            return;
        }

        _commandTimeoutTimer.Stop();
        _commandTimeoutTimer.Start();
        _activeCommand = _bridgeService.EnqueueMediaCommand("play", _sharedMedia.TabId);
        MediaStatusText.Text = "Mengirim perintah Play ke YouTube... (menunggu konfirmasi)";
    }

    private void CommandTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        _commandTimeoutTimer.Stop();
        if (_activeCommand != null)
        {
            LogDiagnostic($"[Media] Command timeout: action={_activeCommand.Action}, id={_activeCommand.Id}");
            MediaStatusText.Text = $"Tidak ada respons dari YouTube untuk perintah {_activeCommand.Action}. Pastikan tab YouTube terbuka.";
            _activeCommand = null;
        }
    }

    private void HandleMediaCommandResult(BridgeMessage result)
    {
        LogDiagnostic($"[Media] Command result received: id={result.CommandId}, action={result.CommandAction}, success={result.CommandSuccess}, error={result.ErrorMessage}");
        _commandTimeoutTimer.Stop();

        if (result.CommandSuccess == true)
        {
            if (result.CommandAction == "pause")
            {
                MediaStatusText.Text = "Playback dijeda di YouTube.";
            }
            else if (result.CommandAction == "play")
            {
                MediaStatusText.Text = "Playback dilanjutkan di YouTube.";
            }
            else if (result.CommandAction == "next")
            {
                MediaStatusText.Text = "Perintah Next berhasil dijalankan di YouTube.";
            }
            else if (result.CommandAction == "prev")
            {
                MediaStatusText.Text = "Perintah Previous berhasil dijalankan di YouTube.";
            }

            if (!string.IsNullOrWhiteSpace(result.PlaybackState))
            {
                if (result.PlaybackState == "paused")
                {
                    HandleMediaPaused(_sharedMedia.Source);
                }
                else if (result.PlaybackState == "playing")
                {
                    HandleMediaPlaying(_sharedMedia.Source, _sharedMedia.Title);
                }
            }
        }
        else
        {
            MediaStatusText.Text = $"Perintah {result.CommandAction} gagal: {result.ErrorMessage ?? "browser tidak dapat mengeksekusi perintah"}";
        }

        _activeCommand = null;
    }

    private void OpenBrowserMedia_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = !string.IsNullOrWhiteSpace(_sharedMedia.PageUrl)
                ? _sharedMedia.PageUrl
                : "https://www.youtube.com";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            LogDiagnostic($"Opened media in browser: {url}");
        }
        catch (Exception ex)
        {
            LogDiagnostic($"Failed to open media in browser: {ex.Message}");
        }
    }

    private void DemoTrackToggle_Click(object sender, RoutedEventArgs e)
    {
        _demoPlaying = !_demoPlaying;
        if (_demoPlaying)
        {
            var track = _demoTracks[_trackIndex];
            _sharedMedia.Source = "Demo";
            _sharedMedia.Title = track.Title;
            _sharedMedia.Channel = track.Artist;
            _sharedMedia.PlaybackState = "playing";
            _sharedMedia.ThumbnailUrl = "";
            _sharedMedia.Duration = 244;
            _sharedMedia.CurrentTime = 30;
            SyncSharedMediaUI();
            ShowMusicActivity();
            _stateManager.SetEmotion(PikoEmotion.Listening);
            MediaStatusText.Text = "Mode Demo visual aktif (simulasi track lokal).";
        }
        else
        {
            _sharedMedia.Reset();
            SyncSharedMediaUI();
            StopMusicActivity();
            _stateManager.SetEmotion(PikoEmotion.Idle);
            MediaStatusText.Text = "Mode Demo dinonaktifkan.";
        }
    }

    private void LyricsButton_Click(object sender, RoutedEventArgs e)
    {
        LyricsPanel.Visibility = LyricsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    // ---------------------------------------------------------
    // CLOCK & FOCUS TIMER (AUTO-INTERVAL POMODORO)
    // ---------------------------------------------------------

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm");

    private void InitPomodoroUI()
    {
        try
        {
            if (PomodoroAutoAdvanceCheck != null)
            {
                PomodoroAutoAdvanceCheck.IsChecked = _state.PomodoroAutoStartNext;
            }

            if (PomodoroLongBreakCombo != null)
            {
                for (int i = 0; i < PomodoroLongBreakCombo.Items.Count; i++)
                {
                    if (PomodoroLongBreakCombo.Items[i] is ComboBoxItem item &&
                        int.TryParse(item.Tag?.ToString(), out int mins) &&
                        mins == _state.PomodoroLongBreakMinutes)
                    {
                        PomodoroLongBreakCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            RefreshPomodoroTasksList();
            UpdatePomodoroCycleBadges();
            UpdateFocusStatusText();
            UpdateFocusDisplay();
        }
        catch (Exception ex)
        {
            LogDiagnostic("InitPomodoroUI-Error", ex);
        }
    }

    private void RefreshPomodoroTasks_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayGentleTap();
        RefreshPomodoroTasksList();
    }

    private void RefreshPomodoroTasksList()
    {
        if (PomodoroTaskCombo == null) return;
        PomodoroTaskCombo.Items.Clear();

        var openTodos = _state.Todos.Where(t => !t.IsDone).ToList();
        foreach (var todo in openTodos)
        {
            PomodoroTaskCombo.Items.Add(todo.Title);
        }

        if (!string.IsNullOrEmpty(_state.PomodoroActiveTask))
        {
            PomodoroTaskCombo.Text = _state.PomodoroActiveTask;
        }
        else if (openTodos.Count > 0)
        {
            PomodoroTaskCombo.SelectedIndex = 0;
            _state.PomodoroActiveTask = openTodos[0].Title;
        }
        else
        {
            PomodoroTaskCombo.Text = "Fokus Belajar / Tugas Utama";
        }
    }

    private void PomodoroTaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PomodoroTaskCombo?.SelectedItem is string selectedTask)
        {
            _state.PomodoroActiveTask = selectedTask;
            SaveAppState();
            UpdateFocusStatusText();
        }
    }

    private void PomodoroSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (PomodoroAutoAdvanceCheck != null)
        {
            _state.PomodoroAutoStartNext = PomodoroAutoAdvanceCheck.IsChecked == true;
        }

        if (PomodoroLongBreakCombo?.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int mins))
        {
            _state.PomodoroLongBreakMinutes = mins;
        }

        SaveAppState();
        UpdatePomodoroCycleBadges();
    }

    private void UpdateFocusStatusText()
    {
        if (FocusStatusText == null) return;
        string task = !string.IsNullOrWhiteSpace(_state.PomodoroActiveTask) ? _state.PomodoroActiveTask : "tugas pilihan Anda";

        if (_pomodoroPhase == PomodoroPhase.Work)
        {
            FocusStatusText.Text = $"Fokus penuh pada: \"{task}\" ✨";
        }
        else if (_pomodoroPhase == PomodoroPhase.ShortBreak)
        {
            FocusStatusText.Text = "Istirahat sejenak 5 menit. Ambil napas, minum air, dan regangkan badan ☕";
        }
        else
        {
            FocusStatusText.Text = $"Istirahat panjang {_state.PomodoroLongBreakMinutes} menit! Waktunya relaksasi setelah 4 sesi penuh 🌴";
        }
    }

    private void UpdatePomodoroCycleBadges()
    {
        if (PomoStep1Border == null || PomoStep2Border == null || PomoStep3Border == null ||
            PomoStep4Border == null || PomoStepLongBorder == null) return;

        bool isDark = _state.DarkMode;
        var activeBg = isDark ? new SolidColorBrush(Color.FromArgb(0x55, 0x7C, 0x3A, 0xED)) : new SolidColorBrush(Color.FromArgb(0x35, 0x7C, 0x3A, 0xED));
        var activeBorder = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
        var activeFg = isDark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");

        var inactiveBg = isDark ? new SolidColorBrush(Color.FromArgb(0x20, 0x1A, 0x16, 0x28)) : new SolidColorBrush(Color.FromArgb(0x25, 0xD4, 0xC7, 0xEC));
        var inactiveBorder = isDark ? new SolidColorBrush(Color.FromArgb(0x35, 0x8B, 0x5C, 0xF6)) : new SolidColorBrush(Color.FromArgb(0x35, 0x5B, 0x3A, 0xA8));
        var inactiveFg = isDark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        var doneBg = isDark ? new SolidColorBrush(Color.FromArgb(0x35, 0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0xB9, 0x81));
        var doneBorder = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        var doneFg = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

        ConfigureStepBadge(PomoStep1Border, PomoStep1Text, 1, activeBg, activeBorder, activeFg, inactiveBg, inactiveBorder, inactiveFg, doneBg, doneBorder, doneFg);
        ConfigureStepBadge(PomoStep2Border, PomoStep2Text, 2, activeBg, activeBorder, activeFg, inactiveBg, inactiveBorder, inactiveFg, doneBg, doneBorder, doneFg);
        ConfigureStepBadge(PomoStep3Border, PomoStep3Text, 3, activeBg, activeBorder, activeFg, inactiveBg, inactiveBorder, inactiveFg, doneBg, doneBorder, doneFg);
        ConfigureStepBadge(PomoStep4Border, PomoStep4Text, 4, activeBg, activeBorder, activeFg, inactiveBg, inactiveBorder, inactiveFg, doneBg, doneBorder, doneFg);

        if (_pomodoroPhase == PomodoroPhase.LongBreak)
        {
            PomoStepLongBorder.Background = activeBg;
            PomoStepLongBorder.BorderBrush = activeBorder;
            PomoStepLongText.Foreground = activeFg;
            PomoStepLongText.FontWeight = FontWeights.Bold;
            PomoStepLongText.Text = $"🌴 Break {_state.PomodoroLongBreakMinutes}m";
        }
        else
        {
            PomoStepLongBorder.Background = inactiveBg;
            PomoStepLongBorder.BorderBrush = inactiveBorder;
            PomoStepLongText.Foreground = inactiveFg;
            PomoStepLongText.FontWeight = FontWeights.Normal;
            PomoStepLongText.Text = $"🌴 Break {_state.PomodoroLongBreakMinutes}m";
        }

        if (PomodoroPhaseBadgeText != null && PomodoroPhaseBadge != null)
        {
            if (_pomodoroPhase == PomodoroPhase.Work)
            {
                PomodoroPhaseBadgeText.Text = $"SESI KERJA #{_pomodoroCycle} (25m)";
                PomodoroPhaseBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
            }
            else if (_pomodoroPhase == PomodoroPhase.ShortBreak)
            {
                PomodoroPhaseBadgeText.Text = "ISTIRAHAT SINGKAT (5m)";
                PomodoroPhaseBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
            }
            else
            {
                PomodoroPhaseBadgeText.Text = $"ISTIRAHAT PANJANG ({_state.PomodoroLongBreakMinutes}m)";
                PomodoroPhaseBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
        }
    }

    private void ConfigureStepBadge(Border border, TextBlock text, int stepNumber,
        Brush activeBg, Brush activeBorder, Brush activeFg,
        Brush inactiveBg, Brush inactiveBorder, Brush inactiveFg,
        Brush doneBg, Brush doneBorder, Brush doneFg)
    {
        if (stepNumber < _pomodoroCycle)
        {
            border.Background = doneBg;
            border.BorderBrush = doneBorder;
            text.Foreground = doneFg;
            text.FontWeight = FontWeights.SemiBold;
            text.Text = $"✓ Sesi {stepNumber}";
        }
        else if (stepNumber == _pomodoroCycle && _pomodoroPhase != PomodoroPhase.LongBreak)
        {
            border.Background = activeBg;
            border.BorderBrush = activeBorder;
            text.Foreground = activeFg;
            text.FontWeight = FontWeights.Bold;
            text.Text = _pomodoroPhase == PomodoroPhase.ShortBreak ? $"Break {stepNumber}" : $"Sesi {stepNumber}";
        }
        else
        {
            border.Background = inactiveBg;
            border.BorderBrush = inactiveBorder;
            text.Foreground = inactiveFg;
            text.FontWeight = FontWeights.Normal;
            text.Text = $"Sesi {stepNumber}";
        }
    }

    private void FocusStartButton_Click(object sender, RoutedEventArgs e)
    {
        _focusRunning = !_focusRunning;
        if (_focusRunning)
        {
            _focusTimer.Start();
            FocusStartButton.Content = "⏸ Jeda";
            UpdateFocusStatusText();
            _stateManager.SetEmotion(PikoEmotion.Listening);
            UpdateFocusDisplay();
            if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned 
                and not DynamicIslandState.AiWorking and not DynamicIslandState.Success 
                and not DynamicIslandState.Notification and not DynamicIslandState.FileEating)
            {
                TransitionToState(DynamicIslandState.PomodoroActive);
            }
        }
        else
        {
            _focusTimer.Stop();
            FocusStartButton.Content = "▶ Lanjutkan";
            FocusStatusText.Text = "Dijeda. Lanjutkan saat Anda siap.";
            _stateManager.SetEmotion(PikoEmotion.Idle);
            if (_currentState == DynamicIslandState.PomodoroActive)
            {
                TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            }
        }
    }

    private void FocusSkipButton_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayGentleTap();
        AdvanceToNextPomodoroInterval(userTriggered: true);
    }

    private void FocusResetButton_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayGentleTap();
        _focusTimer.Stop();
        _focusRunning = false;
        _pomodoroCycle = 1;
        _pomodoroPhase = PomodoroPhase.Work;
        _focusSecondsRemaining = _state.PomodoroWorkDurationMinutes * 60;
        FocusStartButton.Content = "▶ Mulai Fokus (25m)";
        UpdateFocusStatusText();
        UpdatePomodoroCycleBadges();
        UpdateFocusDisplay();
        _stateManager.SetEmotion(PikoEmotion.Idle);
        if (_currentState == DynamicIslandState.PomodoroActive)
        {
            TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
        }
    }

    private void AdvanceToNextPomodoroInterval(bool userTriggered = false)
    {
        if (_pomodoroPhase == PomodoroPhase.Work)
        {
            _soundService.PlaySuccessChime();
            if (_pomodoroCycle < 4)
            {
                _pomodoroPhase = PomodoroPhase.ShortBreak;
                _focusSecondsRemaining = _state.PomodoroShortBreakMinutes * 60;
                _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(5), "Sesi fokus selesai! Waktunya istirahat ☕");
                TriggerIslandNotification(new IslandNotificationItem
                {
                    Priority = NotificationPriority.Timer,
                    Icon = "☕",
                    Title = "Fokus Selesai! Waktunya Istirahat",
                    Message = $"Kerja bagus! Istirahat 5 menit dimulai (Sesi {_pomodoroCycle}/4 selesai). Regangkan badan & minum air ☕",
                    Duration = TimeSpan.FromSeconds(7)
                });
            }
            else
            {
                _pomodoroPhase = PomodoroPhase.LongBreak;
                _focusSecondsRemaining = _state.PomodoroLongBreakMinutes * 60;
                _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(7), "4 Sesi selesai! Luar biasa! 🎉");
                TriggerIslandNotification(new IslandNotificationItem
                {
                    Priority = NotificationPriority.Timer,
                    Icon = "🌴",
                    Title = "4 Sesi Fokus Selesai Penuh! 🎉",
                    Message = $"Luar biasa! Nikmati istirahat panjang {_state.PomodoroLongBreakMinutes} menit untuk memulihkan energi.",
                    Duration = TimeSpan.FromSeconds(8)
                });
            }
        }
        else if (_pomodoroPhase == PomodoroPhase.ShortBreak)
        {
            _soundService.PlayAlertSound();
            _pomodoroCycle++;
            _pomodoroPhase = PomodoroPhase.Work;
            _focusSecondsRemaining = _state.PomodoroWorkDurationMinutes * 60;
            _stateManager.SetEmotion(PikoEmotion.Listening, TimeSpan.FromSeconds(5), $"Mulai Sesi Fokus #{_pomodoroCycle}! ✨");
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Timer,
                Icon = "🍅",
                Title = $"Mulai Sesi Fokus #{_pomodoroCycle}!",
                Message = "Istirahat selesai! Ayo kembali fokus pada tugasmu tanpa distraksi ✨",
                Duration = TimeSpan.FromSeconds(7)
            });
        }
        else // LongBreak
        {
            _soundService.PlaySuccessChime();
            _pomodoroCycle = 1;
            _pomodoroPhase = PomodoroPhase.Work;
            _focusSecondsRemaining = _state.PomodoroWorkDurationMinutes * 60;
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(6), "Siklus Pomodoro lengkap! 🏆");
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Routine,
                Icon = "🏆",
                Title = "Siklus Pomodoro Lengkap!",
                Message = "Satu putaran penuh 4 sesi telah selesai! Mulai siklus baru kapan pun Anda siap ✨",
                Duration = TimeSpan.FromSeconds(8)
            });
        }

        bool autoStart = _state.PomodoroAutoStartNext && !userTriggered;
        if (autoStart)
        {
            _focusRunning = true;
            _focusTimer.Start();
            FocusStartButton.Content = "⏸ Jeda";
            _stateManager.SetEmotion(_pomodoroPhase == PomodoroPhase.Work ? PikoEmotion.Listening : PikoEmotion.Idle);
            if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned 
                and not DynamicIslandState.AiWorking and not DynamicIslandState.Success 
                and not DynamicIslandState.Notification and not DynamicIslandState.FileEating)
            {
                TransitionToState(DynamicIslandState.PomodoroActive);
            }
        }
        else
        {
            _focusRunning = false;
            _focusTimer.Stop();
            FocusStartButton.Content = _pomodoroPhase == PomodoroPhase.Work 
                ? $"▶ Mulai Sesi #{_pomodoroCycle} (25m)" 
                : $"▶ Mulai Istirahat ({_focusSecondsRemaining / 60}m)";
            _stateManager.SetEmotion(PikoEmotion.Idle);
            if (_currentState == DynamicIslandState.PomodoroActive)
            {
                TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
            }
        }

        UpdatePomodoroCycleBadges();
        UpdateFocusStatusText();
        UpdateFocusDisplay();
    }

    private void FocusTimer_Tick(object? sender, EventArgs e)
    {
        if (_focusSecondsRemaining > 0)
        {
            _focusSecondsRemaining--;
            UpdateFocusDisplay();
        }

        if (_focusSecondsRemaining <= 0)
        {
            AdvanceToNextPomodoroInterval(userTriggered: false);
        }
    }

    private void UpdateFocusDisplay()
    {
        var minutes = Math.Max(0, _focusSecondsRemaining) / 60;
        var seconds = Math.Max(0, _focusSecondsRemaining) % 60;
        var formatted = $"{minutes:00}:{seconds:00}";
        FocusTimerText.Text = formatted;
        HomeFocusText.Text = formatted;
        if (IslandPomodoroText != null)
        {
            if (_pomodoroPhase == PomodoroPhase.Work)
            {
                IslandPomodoroText.Text = $"{formatted} #{_pomodoroCycle}";
            }
            else if (_pomodoroPhase == PomodoroPhase.ShortBreak)
            {
                IslandPomodoroText.Text = $"{formatted} Break";
            }
            else
            {
                IslandPomodoroText.Text = $"{formatted} Long";
            }
        }

        if (IslandPomodoroPlayPauseIcon != null)
        {
            IslandPomodoroPlayPauseIcon.Text = _focusRunning ? "⏸" : "▶";
            IslandPomodoroPlayPauseIcon.Margin = _focusRunning ? new Thickness(0, 0, 0, 1) : new Thickness(1, 0, 0, 1);
        }

        if (IslandPomodoroDot != null)
        {
            IslandPomodoroDot.Background = _focusRunning
                ? new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA))
                : new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
    }

    private void IslandPomodoroPlayPause_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _soundService.PlayGentleTap();
        _focusRunning = !_focusRunning;
        if (_focusRunning)
        {
            _focusTimer.Start();
            FocusStartButton.Content = "⏸ Jeda";
            UpdateFocusStatusText();
            _stateManager.SetEmotion(PikoEmotion.Listening);
            UpdateFocusDisplay();
        }
        else
        {
            _focusTimer.Stop();
            FocusStartButton.Content = "▶ Lanjutkan";
            FocusStatusText.Text = "Dijeda. Lanjutkan saat Anda siap.";
            _stateManager.SetEmotion(PikoEmotion.Idle);
            UpdateFocusDisplay();
        }
    }

    private void IslandPomodoro_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _singleClickTimer.Stop();
        e.Handled = true;
        TabFocus.IsChecked = true;
        UpdateTabPanels();
        TransitionToState(DynamicIslandState.Pinned);
    }

    // ---------------------------------------------------------
    // GOOGLE CALENDAR & MULTI-ACCOUNT SYNC
    // ---------------------------------------------------------

    private void InitCalendarUI()
    {
        try
        {
            if (_state.CalendarEvents.Count == 0)
            {
                var samples = _calendarService.GetSampleEvents();
                foreach (var ev in samples)
                {
                    _state.CalendarEvents.Add(ev);
                }
                SaveAppState();
            }

            RenderCalendarUI();
        }
        catch (Exception ex)
        {
            LogDiagnostic("InitCalendarUI-Error", ex);
        }
    }

    private void RenderCalendarUI()
    {
        try
        {
            var idCulture = System.Globalization.CultureInfo.CreateSpecificCulture("id-ID");
            if (CalendarMonthYearText != null)
            {
                CalendarMonthYearText.Text = _currentCalendarMonth.ToString("MMMM yyyy", idCulture);
            }

            if (CalendarTodayHeader != null)
            {
                CalendarTodayHeader.Text = DateTime.Today.ToString("dddd, dd MMMM yyyy", idCulture);
            }

            RenderAccountChips();
            RenderTodayAgenda();
            RenderMonthCalendar();
        }
        catch (Exception ex)
        {
            LogDiagnostic("RenderCalendarUI-Error", ex);
        }
    }

    private void RenderAccountChips()
    {
        if (LinkedAccountsBar == null) return;
        LinkedAccountsBar.Children.Clear();

        bool isDark = _state.DarkMode;
        Brush textMain = isDark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        Brush textMuted = isDark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        if (_state.GoogleAccounts.Count == 0)
        {
            var demoBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x38, 0xBD, 0xF8)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x38, 0xBD, 0xF8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 4)
            };
            var st = new StackPanel { Orientation = Orientation.Horizontal };
            st.Children.Add(new TextBlock { Text = "🔵 Demo Data (Lokal)", FontSize = 11, Foreground = textMuted, VerticalAlignment = VerticalAlignment.Center });
            st.Children.Add(new TextBlock { Text = " · Hubungkan akun Google untuk sinkronkan kalender aslimu", FontSize = 10, Foreground = textMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            demoBadge.Child = st;
            LinkedAccountsBar.Children.Add(demoBadge);
            return;
        }

        foreach (var acc in _state.GoogleAccounts.ToList())
        {
            var chip = new Border
            {
                Background = isDark ? new SolidColorBrush(Color.FromArgb(0x35, 0x7C, 0x3A, 0xED)) : new SolidColorBrush(Color.FromArgb(0x20, 0x7C, 0x3A, 0xED)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0x3A, 0xED)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 3, 6, 3),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(dot);

            var label = new TextBlock
            {
                Text = string.IsNullOrEmpty(acc.DisplayName) ? acc.Email : $"{acc.DisplayName} ({acc.Email})",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = textMain,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(label);

            var removeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                Foreground = textMuted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            removeBtn.Click += (_, _) =>
            {
                if (MessageBox.Show($"Putuskan tautan akun {acc.Email}?", "Hapus Akun Google", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _state.GoogleAccounts.Remove(acc);
                    _state.CalendarEvents.RemoveAll(e => e.AccountEmail == acc.Email);
                    SaveAppState();
                    RenderCalendarUI();
                }
            };
            stack.Children.Add(removeBtn);

            chip.Child = stack;
            LinkedAccountsBar.Children.Add(chip);
        }
    }

    private void RenderTodayAgenda()
    {
        if (TodayAgendaItemsPanel == null) return;
        TodayAgendaItemsPanel.Children.Clear();

        bool isDark = _state.DarkMode;
        Brush textMain = isDark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        Brush textMuted = isDark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        var today = DateTime.Today;
        var todayEvents = _state.CalendarEvents
            .Where(e => e.StartTime.Date == today)
            .OrderBy(e => e.IsAllDay ? 0 : 1)
            .ThenBy(e => e.StartTime)
            .ToList();

        if (TodayEventsCountBadge != null)
        {
            TodayEventsCountBadge.Text = $"{todayEvents.Count} Agenda";
        }

        if (todayEvents.Count == 0)
        {
            var emptyCard = new Border
            {
                Background = isDark ? new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x7C, 0x3A, 0xED)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12)
            };
            var emptyText = new TextBlock
            {
                Text = "✨ Tidak ada agenda tersisa untuk hari ini. Waktunya istirahat atau fokus tugas santai!",
                FontSize = 12,
                Foreground = textMuted
            };
            emptyCard.Child = emptyText;
            TodayAgendaItemsPanel.Children.Add(emptyCard);
            return;
        }

        foreach (var ev in todayEvents)
        {
            var card = new Border
            {
                Background = isDark ? new SolidColorBrush(Color.FromArgb(0x35, 0x20, 0x1B, 0x32)) : new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
                BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6)) : new SolidColorBrush(Color.FromArgb(0x35, 0x7C, 0x3A, 0xED)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // Color bar
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); // Spacing
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            // Color bar
            Color barColor = Color.FromRgb(0x7C, 0x3A, 0xED);
            if (!string.IsNullOrEmpty(ev.ColorHex))
            {
                try { barColor = (Color)ColorConverter.ConvertFromString(ev.ColorHex); } catch { }
            }
            var colorBar = new Border
            {
                Background = new SolidColorBrush(barColor),
                CornerRadius = new CornerRadius(2)
            };
            Grid.SetColumn(colorBar, 0);
            grid.Children.Add(colorBar);

            // Event Details
            var infoStack = new StackPanel();
            var titleBlock = new TextBlock
            {
                Text = ev.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = textMain,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            infoStack.Children.Add(titleBlock);

            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            string timeStr = ev.IsAllDay ? "Seharian (All Day)" : $"{ev.StartTime:HH:mm} - {ev.EndTime:HH:mm}";
            var timeBlock = new TextBlock
            {
                Text = $"🕒 {timeStr}",
                FontSize = 11,
                Foreground = (Brush)FindResource("LavenderBrush"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            metaStack.Children.Add(timeBlock);

            if (!string.IsNullOrEmpty(ev.AccountEmail))
            {
                var accBlock = new TextBlock
                {
                    Text = $"👤 {ev.AccountEmail}",
                    FontSize = 10,
                    Foreground = textMuted
                };
                metaStack.Children.Add(accBlock);
            }
            infoStack.Children.Add(metaStack);

            if (!string.IsNullOrEmpty(ev.Description))
            {
                var descBlock = new TextBlock
                {
                    Text = ev.Description,
                    FontSize = 10.5,
                    Foreground = textMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 16,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                infoStack.Children.Add(descBlock);
            }

            Grid.SetColumn(infoStack, 2);
            grid.Children.Add(infoStack);

            // Actions (Google Meet or Link)
            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(ev.MeetLink))
            {
                var meetBtn = new Button
                {
                    Content = "🎥 Google Meet",
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(4, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                meetBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(ev.MeetLink) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        LogDiagnostic("OpenMeetLink-Error", ex);
                    }
                };
                actionStack.Children.Add(meetBtn);
            }

            if (!string.IsNullOrEmpty(ev.HtmlLink))
            {
                var linkBtn = new Button
                {
                    Content = "🔗 Buka",
                    FontSize = 10.5,
                    Foreground = (Brush)FindResource("LavenderBrush"),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0x3A, 0xED)),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(4, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                linkBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(ev.HtmlLink) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        LogDiagnostic("OpenHtmlLink-Error", ex);
                    }
                };
                actionStack.Children.Add(linkBtn);
            }

            Grid.SetColumn(actionStack, 3);
            grid.Children.Add(actionStack);

            card.Child = grid;
            TodayAgendaItemsPanel.Children.Add(card);
        }
    }

    private void RenderMonthCalendar()
    {
        if (CalendarMonthGrid == null) return;
        CalendarMonthGrid.Children.Clear();

        bool isDark = _state.DarkMode;
        var firstOfMonth = new DateTime(_currentCalendarMonth.Year, _currentCalendarMonth.Month, 1);
        int dayOfWeekOffset = (int)firstOfMonth.DayOfWeek; // 0 = Sunday
        var startDate = firstOfMonth.AddDays(-dayOfWeekOffset);

        // Border line for day cells (sharp and distinct)
        var cellBorderBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(0x55, 0x8B, 0x5C, 0xF6))
            : new SolidColorBrush(Color.FromArgb(0x40, 0x5B, 0x3A, 0xA8));

        var textPrimaryBrush = isDark
            ? new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFD))
            : new SolidColorBrush(Color.FromRgb(0x24, 0x20, 0x44));

        var textMutedBrush = isDark
            ? new SolidColorBrush(Color.FromArgb(0x60, 0x9B, 0x93, 0xB0))
            : new SolidColorBrush(Color.FromArgb(0x75, 0x75, 0x6F, 0x93));

        for (int i = 0; i < 42; i++)
        {
            var cellDate = startDate.AddDays(i);
            bool isCurrentMonth = cellDate.Month == _currentCalendarMonth.Month;
            bool isToday = cellDate.Date == DateTime.Today;

            Brush cellBg;
            if (isToday)
            {
                cellBg = isDark
                    ? new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0x3A, 0xED))
                    : new SolidColorBrush(Color.FromArgb(0x28, 0x7C, 0x3A, 0xED));
            }
            else if (isCurrentMonth)
            {
                cellBg = isDark
                    ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                cellBg = isDark
                    ? new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00))
                    : new SolidColorBrush(Color.FromArgb(0x50, 0xF5, 0xF2, 0xFC));
            }

            var cellBorder = new Border
            {
                BorderBrush = cellBorderBrush,
                BorderThickness = new Thickness(0.5),
                Background = cellBg,
                Padding = new Thickness(4, 3, 4, 3),
                MinHeight = 46,
                Cursor = Cursors.Hand
            };

            var cellStack = new StackPanel();

            // Date Number header
            var numPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 0, 0, 2) };
            if (isToday)
            {
                var todayBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)),
                    CornerRadius = new CornerRadius(9),
                    Width = 19,
                    Height = 19,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var todayNum = new TextBlock
                {
                    Text = cellDate.Day.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                todayBadge.Child = todayNum;
                numPanel.Children.Add(todayBadge);
            }
            else
            {
                var numBlock = new TextBlock
                {
                    Text = (cellDate.Day == 1 ? cellDate.ToString("MMM d") : cellDate.Day.ToString()),
                    FontSize = 10,
                    FontWeight = isCurrentMonth ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isCurrentMonth ? textPrimaryBrush : textMutedBrush
                };
                numPanel.Children.Add(numBlock);
            }
            cellStack.Children.Add(numPanel);

            // Filter events for this cellDate
            var dayEvents = _state.CalendarEvents
                .Where(e => e.StartTime.Date == cellDate.Date)
                .OrderBy(e => e.IsAllDay ? 0 : 1)
                .ThenBy(e => e.StartTime)
                .ToList();

            int displayCount = Math.Min(dayEvents.Count, 2);
            for (int eIdx = 0; eIdx < displayCount; eIdx++)
            {
                var ev = dayEvents[eIdx];
                Color chipBgColor = Color.FromRgb(0x7C, 0x3A, 0xED);
                if (!string.IsNullOrEmpty(ev.ColorHex))
                {
                    try { chipBgColor = (Color)ColorConverter.ConvertFromString(ev.ColorHex); } catch { }
                }

                var eventChip = new Border
                {
                    Background = new SolidColorBrush(chipBgColor),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1.5, 4, 1.5),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{ev.Title}\n🕒 {ev.StartTime:HH:mm} - {ev.EndTime:HH:mm}\n👤 {ev.AccountEmail}" +
                              (!string.IsNullOrEmpty(ev.Description) ? $"\n📝 {ev.Description}" : "")
                };

                string timeSnippet = ev.IsAllDay ? "" : ev.StartTime.ToString("HH:mm") + " ";
                var chipText = new TextBlock
                {
                    Text = $"{timeSnippet}{ev.Title}",
                    FontSize = 8.5,
                    FontWeight = FontWeights.Medium,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 14
                };
                eventChip.Child = chipText;

                eventChip.MouseLeftButtonUp += (_, _) => ShowSelectedDayDetail(cellDate, dayEvents);

                cellStack.Children.Add(eventChip);
            }

            if (dayEvents.Count > 2)
            {
                var morePill = new Border
                {
                    Background = isDark 
                        ? new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6))
                        : new SolidColorBrush(Color.FromArgb(0x25, 0x7C, 0x3A, 0xED)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(0, 1, 0, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = string.Join("\n", dayEvents.Skip(2).Select(e => $"• {e.Title} ({e.StartTime:HH:mm})"))
                };
                var moreText = new TextBlock
                {
                    Text = $"+{dayEvents.Count - 2} lagi",
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isDark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("DeepVioletBrush")
                };
                morePill.Child = moreText;
                morePill.MouseLeftButtonUp += (_, _) => ShowSelectedDayDetail(cellDate, dayEvents);
                cellStack.Children.Add(morePill);
            }

            cellBorder.Child = cellStack;
            cellBorder.MouseLeftButtonUp += (_, _) => ShowSelectedDayDetail(cellDate, dayEvents);
            CalendarMonthGrid.Children.Add(cellBorder);
        }
    }

    private void ShowSelectedDayDetail(DateTime date, List<CalendarEventItem> events)
    {
        if (SelectedDayDetailBorder == null || SelectedDayTitleText == null || SelectedDayEventsPanel == null) return;
        SelectedDayDetailBorder.Visibility = Visibility.Visible;
        SelectedDayTitleText.Text = $"Agenda: {date:dddd, dd MMMM yyyy}";
        SelectedDayEventsPanel.Children.Clear();

        bool isDark = _state.DarkMode;
        Brush textMain = isDark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        Brush textMuted = isDark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");

        if (events.Count == 0)
        {
            SelectedDayEventsPanel.Children.Add(new TextBlock
            {
                Text = "Tidak ada agenda di hari ini.",
                FontSize = 11,
                Foreground = textMuted,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        foreach (var ev in events)
        {
            var p = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
            var h = new TextBlock { Text = $"• {ev.Title}", FontWeight = FontWeights.SemiBold, FontSize = 11.5, Foreground = textMain };
            string timeStr = ev.IsAllDay ? "Seharian" : $"{ev.StartTime:HH:mm} - {ev.EndTime:HH:mm}";
            var sub = new TextBlock { Text = $"  {timeStr} · {ev.AccountEmail}", FontSize = 10, Foreground = (Brush)FindResource("LavenderBrush") };
            p.Children.Add(h);
            p.Children.Add(sub);

            if (!string.IsNullOrEmpty(ev.MeetLink))
            {
                var mBtn = new Button
                {
                    Content = "🎥 Buka Google Meet",
                    FontSize = 10,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(8, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                mBtn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(ev.MeetLink) { UseShellExecute = true }); } catch { } };
                p.Children.Add(mBtn);
            }

            SelectedDayEventsPanel.Children.Add(p);
        }
    }

    private void CloseSelectedDayDetail_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDayDetailBorder != null) SelectedDayDetailBorder.Visibility = Visibility.Collapsed;
    }

    private void CalendarToday_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RenderCalendarUI();
        _soundService.PlayGentleTap();
    }

    private void CalendarPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarMonth = _currentCalendarMonth.AddMonths(-1);
        RenderCalendarUI();
        _soundService.PlayGentleTap();
    }

    private void CalendarNextMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarMonth = _currentCalendarMonth.AddMonths(1);
        RenderCalendarUI();
        _soundService.PlayGentleTap();
    }

    private void AddGoogleAccount_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayGentleTap();
        if (AddAccountModalBorder.Visibility == Visibility.Visible)
        {
            AddAccountModalBorder.Visibility = Visibility.Collapsed;
            return;
        }

        AddAccountModalBorder.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(IcalAccountEmailInput.Text))
        {
            IcalAccountEmailInput.Text = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(OAuthClientIdInput.Text) && !string.IsNullOrWhiteSpace(_state.GoogleClientId))
        {
            OAuthClientIdInput.Text = _state.GoogleClientId;
        }
        if (string.IsNullOrWhiteSpace(OAuthClientSecretInput.Text) && !string.IsNullOrWhiteSpace(_state.GoogleClientSecret))
        {
            OAuthClientSecretInput.Text = _state.GoogleClientSecret;
        }

        IcalUrlInput.Focus();
    }

    private void CloseAddAccountModal_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayGentleTap();
        AddAccountModalBorder.Visibility = Visibility.Collapsed;
    }

    private void SyncMode_Changed(object sender, RoutedEventArgs e)
    {
        if (IcalModeContainer == null || OAuthModeContainer == null) return;
        if (SyncModeIcalRadio?.IsChecked == true)
        {
            IcalModeContainer.Visibility = Visibility.Visible;
            OAuthModeContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            IcalModeContainer.Visibility = Visibility.Collapsed;
            OAuthModeContainer.Visibility = Visibility.Visible;
        }
    }

    private void OpenGoogleCalendarWeb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _soundService.PlayGentleTap();
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://calendar.google.com/calendar/u/0/r/settings",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogDiagnostic("OpenGoogleCalendarWeb_Click-Error", ex);
        }
    }

    private async void SaveIcalAccount_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        object originalContent = btn?.Content;
        try
        {
            string url = IcalUrlInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Silakan masukkan URL Alamat Rahasia iCal dari Google Calendar Anda terlebih dahulu.", "URL iCal Diperlukan", MessageBoxButton.OK, MessageBoxImage.Warning);
                IcalUrlInput.Focus();
                return;
            }

            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url.Substring(9);
            }
            else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Mengunduh Agenda...";
            }

            string email = IcalAccountEmailInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                email = "Google Calendar";
            }

            string[] colors = { "#A182F9", "#4ADE80", "#38BDF8", "#F472B6", "#FBBF24" };
            string color = colors[_state.GoogleAccounts.Count % colors.Length];

            var account = new GoogleAccountEntry
            {
                Email = email,
                SyncMethod = "iCal",
                IcalUrl = url,
                AccountColorHex = color
            };

            var events = await _calendarService.FetchEventsFromIcalUrlAsync(account);

            _state.GoogleAccounts.RemoveAll(a => a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
            _state.GoogleAccounts.Add(account);

            _state.CalendarEvents.RemoveAll(ev => ev.AccountEmail.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
            foreach (var ev in events)
            {
                _state.CalendarEvents.Add(ev);
            }

            SaveAppState();
            RenderCalendarUI();
            AddAccountModalBorder.Visibility = Visibility.Collapsed;

            _soundService.PlaySuccessChime();

            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Routine,
                Icon = "📅",
                Title = "Kalender Terhubung!",
                Message = $"{events.Count} agenda dari {account.Email} berhasil dimuat.",
                Duration = TimeSpan.FromSeconds(5)
            });

            MessageBox.Show($"Berhasil menghubungkan kalender '{account.Email}'!\n\n{events.Count} agenda berhasil disinkronkan ke Piko Companion.", "Kalender Berhasil Ditautkan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogDiagnostic("SaveIcalAccount_Click-Error", ex);
            MessageBox.Show($"Gagal menyinkronkan kalender dari URL iCal:\n{ex.Message}\n\nPastikan Anda menyalin 'Alamat rahasia dalam format iCal' dari Setelan Google Calendar.", "Error iCal", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = originalContent;
            }
        }
    }

    private async void StartOAuthFlow_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        object originalContent = btn?.Content;
        try
        {
            string clientId = OAuthClientIdInput.Text?.Trim() ?? string.Empty;
            string clientSecret = OAuthClientSecretInput.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                MessageBox.Show("Untuk menggunakan OAuth 2.0, Anda wajib mengisi Client ID yang dibuat di Google Cloud Console.\n\n💡 Rekomendasi: Gunakan tab 'Format iCal URL' di atas untuk cara instan tanpa perlu mendaftar di Google Cloud Console!", "Client ID Diperlukan", MessageBoxButton.OK, MessageBoxImage.Warning);
                OAuthClientIdInput.Focus();
                return;
            }

            if (btn != null)
            {
                btn.IsEnabled = false;
                btn.Content = "⏳ Menghubungkan Browser...";
            }

            _state.GoogleClientId = clientId;
            _state.GoogleClientSecret = clientSecret;

            var account = await _calendarService.AuthenticateWithLoopbackAsync(clientId, clientSecret);
            if (account != null)
            {
                _state.GoogleAccounts.RemoveAll(a => a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
                _state.GoogleAccounts.Add(account);

                var newEvents = await _calendarService.FetchEventsAsync(account);
                _state.CalendarEvents.RemoveAll(ev => ev.AccountEmail.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
                foreach (var ev in newEvents)
                {
                    _state.CalendarEvents.Add(ev);
                }

                SaveAppState();
                RenderCalendarUI();
                AddAccountModalBorder.Visibility = Visibility.Collapsed;

                TriggerIslandNotification(new IslandNotificationItem
                {
                    Priority = NotificationPriority.Routine,
                    Icon = "📅",
                    Title = "Google Calendar Terhubung",
                    Message = $"Akun {account.Email} berhasil ditautkan!",
                    Duration = TimeSpan.FromSeconds(5)
                });

                _soundService.PlaySuccessChime();
                MessageBox.Show($"Akun Google '{account.Email}' berhasil ditautkan ke Piko Companion! Agenda telah disinkronkan. ✨", "Google Calendar Connected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Gagal menautkan Google Calendar atau waktu habis.\n\nPastikan Client ID sudah terdaftar di Google Cloud Console dengan redirect URI http://127.0.0.1:5000/.", "OAuth Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("StartOAuthFlow_Click-Error", ex);
            MessageBox.Show($"Terjadi kesalahan: {ex.Message}", "Google Calendar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (btn != null)
            {
                btn.IsEnabled = true;
                btn.Content = originalContent;
            }
        }
    }

    private async void SyncCalendar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncCalendarButton.IsEnabled = false;
            SyncCalendarButton.Content = "⏳ Sinkronisasi...";

            if (_state.GoogleAccounts.Count == 0)
            {
                var samples = _calendarService.GetSampleEvents();
                _state.CalendarEvents.Clear();
                foreach (var ev in samples)
                {
                    _state.CalendarEvents.Add(ev);
                }
                SaveAppState();
                RenderCalendarUI();
                MessageBox.Show("Agenda demo diperbarui! Hubungkan akun Google untuk menyinkronkan agenda aslimu secara langsung. ✨", "Piko Calendar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int syncedCount = 0;
            foreach (var acc in _state.GoogleAccounts)
            {
                var events = await _calendarService.FetchEventsAsync(acc);
                if (events.Count > 0)
                {
                    _state.CalendarEvents.RemoveAll(ev => ev.AccountEmail.Equals(acc.Email, StringComparison.OrdinalIgnoreCase));
                    foreach (var ev in events)
                    {
                        _state.CalendarEvents.Add(ev);
                    }
                    syncedCount += events.Count;
                }
            }

            SaveAppState();
            RenderCalendarUI();
            _soundService.PlaySuccessChime();

            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Routine,
                Icon = "🔄",
                Title = "Kalender Disinkronkan",
                Message = $"{syncedCount} agenda berhasil diperbarui dari Google Calendar.",
                Duration = TimeSpan.FromSeconds(4)
            });
        }
        catch (Exception ex)
        {
            LogDiagnostic("SyncCalendar_Click-Error", ex);
            MessageBox.Show($"Gagal sinkronisasi kalender: {ex.Message}", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SyncCalendarButton.IsEnabled = true;
            SyncCalendarButton.Content = "🔄 Sinkron";
        }
    }

    private void SaveGoogleApiConfig_Click(object sender, RoutedEventArgs e)
    {
        if (GoogleClientIdInput != null) _state.GoogleClientId = GoogleClientIdInput.Text.Trim();
        if (GoogleClientSecretInput != null) _state.GoogleClientSecret = GoogleClientSecretInput.Text.Trim();
        SaveAppState();
        MessageBox.Show("Konfigurasi Google OAuth Client ID & Secret berhasil disimpan! ✨", "Piko Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SettingsTestAiBridge_Click(object sender, RoutedEventArgs e)
    {
        bool isRunning = _bridgeService.IsRunning;
        if (isRunning)
        {
            MessageBox.Show("Piko AI Bridge aktif dan siap menerima data dari Browser Extension & Obsidian Bridge! 🚀", "AI Bridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Piko AI Bridge sedang tidak berjalan. Silakan restart Piko.", "AI Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------------------------------------------------
    // WATER REMINDER & DAILY HYDRATION
    // ---------------------------------------------------------

    private void WaterReminderTimer_Tick(object? sender, EventArgs e)
    {
        if (!_state.WaterReminderEnabled || _isWaterAlertActive) return;

        if (_waterSecondsRemaining > 0)
        {
            _waterSecondsRemaining--;
        }

        if (WaterCountdownText != null && (PanelWater?.IsVisible == true || _currentState is DynamicIslandState.Expanded or DynamicIslandState.Pinned))
        {
            int m = _waterSecondsRemaining / 60;
            int s = _waterSecondsRemaining % 60;
            WaterCountdownText.Text = $"⏳ Pengingat berikutnya dalam: {m:D2}:{s:D2}";
        }

        if (_waterSecondsRemaining <= 0)
        {
            _waterSecondsRemaining = 0;
            TriggerWaterReminderAlert();
        }
    }

    private void TriggerWaterReminderAlert()
    {
        _isWaterAlertActive = true;
        _waterChimeRepeatTimer.Stop();
        _waterChimeRepeatTimer.Start();
        _soundService.PlayAlertSound();
        LogDiagnostic("Water reminder alert triggered.");
        if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned)
        {
            TransitionToState(DynamicIslandState.WaterReminder);
        }
    }

    public void DismissWaterReminderAndDrink()
    {
        _isWaterAlertActive = false;
        _waterChimeRepeatTimer.Stop();

        if (_state.WaterDate.Date != DateTime.Today)
        {
            _state.WaterDate = DateTime.Today;
            _state.WaterCount = 0;
        }

        _state.WaterCount++;
        _state.LastDrinkTime = DateTime.Now;
        _waterSecondsRemaining = Math.Max(15, _state.WaterReminderIntervalMinutes) * 60;
        SaveAppState();
        UpdateWaterUI();

        _soundService.PlaySuccessChime();
        _eyeController?.TriggerBounce();

        TriggerIslandNotification(new IslandNotificationItem
        {
            Priority = NotificationPriority.Routine,
            Icon = "💧",
            Title = "Segar! ✨",
            Message = $"Gelas ke-{_state.WaterCount} hari ini. Pengingat direset ke {_state.WaterReminderIntervalMinutes}m.",
            Duration = TimeSpan.FromSeconds(3)
        });

        LogDiagnostic($"DismissWaterReminderAndDrink: count={_state.WaterCount}, next in {_state.WaterReminderIntervalMinutes}m");
    }

    private void IslandWaterReminder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _singleClickTimer.Stop();
        e.Handled = true;
        DismissWaterReminderAndDrink();
    }

    private void InitWaterReminderUI()
    {
        if (_state.WaterReminderIntervalMinutes < 15) _state.WaterReminderIntervalMinutes = 45;
        if (WaterIntervalSlider != null)
        {
            WaterIntervalSlider.Value = _state.WaterReminderIntervalMinutes;
        }
        if (WaterIntervalLabel != null)
        {
            WaterIntervalLabel.Text = $"{_state.WaterReminderIntervalMinutes} menit";
        }
        UpdateWaterReminderToggleBtn();
        HighlightWaterPresetBtn(_state.WaterReminderIntervalMinutes);
        UpdateWaterUI();
    }

    private void UpdateWaterReminderToggleBtn()
    {
        if (WaterReminderToggleBtn != null)
        {
            WaterReminderToggleBtn.Content = _state.WaterReminderEnabled ? "🔔 Pengingat: Aktif" : "🔕 Pengingat: Nonaktif";
            WaterReminderToggleBtn.Background = _state.WaterReminderEnabled
                ? new SolidColorBrush(Color.FromRgb(240, 253, 244))
                : new SolidColorBrush(Color.FromRgb(243, 244, 246));
            WaterReminderToggleBtn.Foreground = _state.WaterReminderEnabled
                ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                : new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }
        if (WaterCountdownBox != null)
        {
            WaterCountdownBox.Opacity = _state.WaterReminderEnabled ? 1.0 : 0.45;
        }
    }

    private void WaterReminderToggle_Click(object sender, RoutedEventArgs e)
    {
        _state.WaterReminderEnabled = !_state.WaterReminderEnabled;
        if (_state.WaterReminderEnabled)
        {
            _waterSecondsRemaining = _state.WaterReminderIntervalMinutes * 60;
        }
        else
        {
            _isWaterAlertActive = false;
            _waterChimeRepeatTimer.Stop();
            if (_currentState == DynamicIslandState.WaterReminder)
            {
                TransitionToState(DynamicIslandState.Idle);
            }
        }
        SaveAppState();
        UpdateWaterReminderToggleBtn();
        _soundService.PlayGentleTap();
    }

    private void WaterPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var minutes))
        {
            _state.WaterReminderIntervalMinutes = minutes;
            _waterSecondsRemaining = minutes * 60;
            if (WaterIntervalSlider != null) WaterIntervalSlider.Value = minutes;
            if (WaterIntervalLabel != null) WaterIntervalLabel.Text = $"{minutes} menit";
            HighlightWaterPresetBtn(minutes);
            SaveAppState();
            _soundService.PlayGentleTap();
        }
    }

    private void HighlightWaterPresetBtn(int minutes)
    {
        var presets = new (Button? btn, int val)[]
        {
            (WaterPreset30Btn, 30),
            (WaterPreset45Btn, 45),
            (WaterPreset60Btn, 60),
            (WaterPreset90Btn, 90)
        };
        foreach (var (btn, val) in presets)
        {
            if (btn == null) continue;
            if (val == minutes)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(237, 229, 255));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(124, 58, 237));
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 167, 139, 250));
                btn.Foreground = (Brush)(TryFindResource("MainTextBrush") ?? Brushes.Black);
                btn.FontWeight = FontWeights.Normal;
            }
        }
    }

    private void WaterIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WaterIntervalLabel == null) return;
        int minutes = (int)e.NewValue;
        _state.WaterReminderIntervalMinutes = minutes;
        WaterIntervalLabel.Text = $"{minutes} menit";
        HighlightWaterPresetBtn(minutes);
    }

    private void TestWaterReminder_Click(object sender, RoutedEventArgs e)
    {
        TriggerWaterReminderAlert();
    }

    private void ResetWater_Click(object sender, RoutedEventArgs e)
    {
        _state.WaterCount = 0;
        _state.WaterDate = DateTime.Today;
        _waterSecondsRemaining = _state.WaterReminderIntervalMinutes * 60;
        SaveAppState();
        UpdateWaterUI();
        _soundService.PlayGentleTap();
    }

    private void AddWater_Click(object sender, RoutedEventArgs e)
    {
        DismissWaterReminderAndDrink();
    }

    private void UpdateWaterUI()
    {
        if (_state.WaterDate.Date != DateTime.Today)
        {
            _state.WaterDate = DateTime.Today;
            _state.WaterCount = 0;
        }

        var text = $"{_state.WaterCount} / 8 glasses";
        if (WaterCountText != null) WaterCountText.Text = text;
        if (HomeWaterText != null) HomeWaterText.Text = text;

        var pct = Math.Min(100, _state.WaterCount / 8.0 * 100);
        if (WaterProgress != null) WaterProgress.Value = pct;
        if (HomeWaterProgress != null) HomeWaterProgress.Value = pct;

        if (WaterGlassesIcons != null)
        {
            int full = Math.Min(8, _state.WaterCount);
            int empty = Math.Max(0, 8 - full);
            WaterGlassesIcons.Text = string.Concat(Enumerable.Repeat("🥛", full)) + string.Concat(Enumerable.Repeat("▫️", empty));
        }

        if (WaterHintText != null)
        {
            WaterHintText.Text = _state.WaterCount >= 8
                ? "Luar biasa! Target 8 gelas hidrasi harian tercapai ✨"
                : "Pengingat lembut untuk hidrasi, tetap jaga kesehatan.";
        }
    }

    // ---------------------------------------------------------
    // TO-DO LIST
    // ---------------------------------------------------------

    private void TodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTodo();
            e.Handled = true;
        }
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        var title = TodoInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;

        DateTime? dueDate = DueCombo.SelectedIndex switch
        {
            1 => DateTime.Today,
            2 => DateTime.Today.AddDays(1),
            3 => DateTime.Today.AddDays(7),
            _ => null
        };

        _state.Todos.Add(new TodoEntry { Title = title, DueDate = dueDate, IsDone = false });
        TodoInput.Clear();
        DueCombo.SelectedIndex = 0;
        SaveAppState();
        UpdateTodoList();
    }

    private void UpdateTodoList()
    {
        TodoItemsPanel.Children.Clear();
        var activeCount = _state.Todos.Count(t => !t.IsDone);
        TodoCountText.Text = $"{activeCount} open";
        HomeTasksCountText.Text = $"{activeCount} tugas terbuka";

        var firstActive = _state.Todos.FirstOrDefault(t => !t.IsDone);
        HomeTopTaskText.Text = firstActive is not null ? firstActive.Title : "Tidak ada tugas tertunda.";

        bool dark = _state.DarkMode;
        var textMain = dark ? (Brush)FindResource("Dark_TextPrimaryBrush") : (Brush)FindResource("MainTextBrush");
        var textMuted = dark ? (Brush)FindResource("Dark_TextSecondaryBrush") : (Brush)FindResource("MutedBrush");
        var cardBg = dark ? new SolidColorBrush(Color.FromArgb(0x40, 0x2A, 0x22, 0x3E)) : new SolidColorBrush(Color.FromArgb(0x60, 0xF5, 0xF0, 0xFF));
        var cardBorder = dark ? new SolidColorBrush(Color.FromArgb(0x35, 0xB8, 0x92, 0xFA)) : (Brush)FindResource("LavenderBrush");
        var doneText = dark ? new SolidColorBrush(Color.FromArgb(0x80, 0x9B, 0x93, 0xB0)) : new SolidColorBrush(Color.FromArgb(0x85, 0x75, 0x6F, 0x93));

        var inputFg = dark ? Brushes.White : Brushes.Black;
        var inputBg = dark
            ? new SolidColorBrush(Color.FromArgb(0xEA, 0x1A, 0x16, 0x28))
            : new SolidColorBrush(Color.FromArgb(0xFA, 0xFF, 0xFF, 0xFF));
        var inputBorder = dark ? (Brush)FindResource("Dark_BorderBrush") : (Brush)FindResource("LightPurpleBrush");

        if (TodoInput != null)
        {
            TodoInput.Background = inputBg;
            TodoInput.BorderBrush = inputBorder;
            TodoInput.Foreground = inputFg;
            TodoInput.CaretBrush = inputFg;
            TextElement.SetForeground(TodoInput, inputFg);
        }
        if (DueCombo != null)
        {
            DueCombo.Background = inputBg;
            DueCombo.BorderBrush = inputBorder;
            DueCombo.Foreground = inputFg;
            TextElement.SetForeground(DueCombo, inputFg);
            foreach (var item in DueCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    cbi.Foreground = inputFg;
                    TextElement.SetForeground(cbi, inputFg);
                }
            }
        }

        if (_state.Todos.Count == 0)
        {
            TodoItemsPanel.Children.Add(new TextBlock
            {
                Text = "Daftar tugas kosong. Nikmati harimu dengan tenang.",
                Foreground = textMuted,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var item in _state.Todos.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToList())
        {
            var taskCard = new Border
            {
                Background = cardBg,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2.5, 0, 2.5)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = item.IsDone,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                ToolTip = "Tandai selesai"
            };
            cb.Checked += (_, _) => SetTodoDone(item, true);
            cb.Unchecked += (_, _) => SetTodoDone(item, false);
            Grid.SetColumn(cb, 0);
            row.Children.Add(cb);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
            var titleBlock = new TextBlock
            {
                Text = item.Title,
                Foreground = item.IsDone ? doneText : textMain,
                FontSize = 11.5,
                FontWeight = item.IsDone ? FontWeights.Normal : FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null
            };
            sp.Children.Add(titleBlock);

            if (item.DueDate.HasValue)
            {
                var isOverdue = !item.IsDone && item.DueDate.Value.Date < DateTime.Today;
                var dueLabel = isOverdue
                    ? $"⚠️ Terlambat · {item.DueDate.Value:dd MMM}"
                    : item.DueDate.Value.Date == DateTime.Today
                        ? "📅 Hari ini"
                        : item.DueDate.Value.Date == DateTime.Today.AddDays(1)
                            ? "📅 Besok"
                            : $"📅 {item.DueDate.Value:dd MMM}";

                var dueBrush = isOverdue
                    ? (dark ? new SolidColorBrush(Color.FromRgb(0xFA, 0x70, 0x70)) : Brushes.OrangeRed)
                    : (dark ? (Brush)FindResource("LavenderBrush") : (Brush)FindResource("PrimaryVioletBrush"));

                sp.Children.Add(new TextBlock
                {
                    Text = dueLabel,
                    Foreground = item.IsDone ? doneText : dueBrush,
                    FontSize = 9.5,
                    FontWeight = isOverdue ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            Grid.SetColumn(sp, 1);
            row.Children.Add(sp);

            var delBtn = new Button
            {
                Content = "×",
                Style = (Style)FindResource("PikoButton"),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Hapus tugas"
            };
            delBtn.Click += (_, _) =>
            {
                _state.Todos.Remove(item);
                SaveAppState();
                UpdateTodoList();
            };
            Grid.SetColumn(delBtn, 2);
            row.Children.Add(delBtn);

            taskCard.Child = row;
            TodoItemsPanel.Children.Add(taskCard);
        }
    }

    private void SetTodoDone(TodoEntry item, bool done)
    {
        if (item.IsDone == done) return;
        item.IsDone = done;
        SaveAppState();
        UpdateTodoList();
        if (done) _soundService.PlayGentleTap();
    }

    // ---------------------------------------------------------
    // FILE SHELF, DRAG & DROP, SCREENSHOT AUTO-CAPTURE & RENAME
    // ---------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);
    private static readonly uint WM_PIKO_SHOW = RegisterWindowMessage("PIKO_SHOW_DYNAMIC_ISLAND");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    private IntPtr _windowHandle = IntPtr.Zero;
    private FileSystemWatcher? _screenshotWatcher;
    private DateTime _lastClipboardCapture = DateTime.MinValue;
    private DynamicIslandState _preDragState = DynamicIslandState.Idle;
    private bool _isDraggingFileOver;
    private string? _renamingFilePath;
    private readonly HashSet<string> _selectedShelfFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _shelfAnchorPath;
    private string? _selectedShelfFilePath => _selectedShelfFilePaths.FirstOrDefault();

    private void SetupNativeListeners()
    {
        try
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(WndProc);

            AddClipboardFormatListener(_windowHandle);

            // Allow drag-drop from Windows Explorer across integrity levels
            ChangeWindowMessageFilterEx(_windowHandle, 0x0233, 1, IntPtr.Zero); // WM_DROPFILES
            ChangeWindowMessageFilterEx(_windowHandle, 0x004A, 1, IntPtr.Zero); // WM_COPYDATA
            ChangeWindowMessageFilterEx(_windowHandle, 0x0049, 1, IntPtr.Zero); // WM_COPYGLOBALDATA
            if (WM_PIKO_SHOW != 0)
            {
                ChangeWindowMessageFilterEx(_windowHandle, WM_PIKO_SHOW, 1, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("SetupNativeListeners-Error", ex);
        }

        InitializeScreenshotWatcher();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0 && (uint)msg == WM_PIKO_SHOW)
        {
            Dispatcher.Invoke(ShowAndExpandToForeground);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == 0x031D) // WM_CLIPBOARDUPDATE
        {
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private void InitializeScreenshotWatcher()
    {
        try
        {
            var myPictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var screenshotsDir = Path.Combine(myPictures, "Screenshots");
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }

            _screenshotWatcher = new FileSystemWatcher(screenshotsDir)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _screenshotWatcher.Created += ScreenshotWatcher_Created;
        }
        catch (Exception ex)
        {
            LogDiagnostic("ScreenshotWatcher-Init-Error", ex);
        }
    }

    private async void ScreenshotWatcher_Created(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return;

        // Allow Windows time to write the screenshot file
        await Task.Delay(600);

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (File.Exists(e.FullPath) && !_state.ShelfFiles.Contains(e.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    _state.ShelfFiles.Add(e.FullPath);
                    SaveAppState();
                    UpdateShelfUI();
                    _soundService.PlaySuccessChime();

                    ShowScreenshotNotification(Path.GetFileName(e.FullPath));
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic("ScreenshotWatcher-Created-Error", ex);
            }
        });
    }

    private void OnClipboardUpdate()
    {
        // Debounce to prevent duplicate triggers
        if ((DateTime.Now - _lastClipboardCapture).TotalMilliseconds < 1200) return;

        try
        {
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image == null) return;

                _lastClipboardCapture = DateTime.Now;

                var pikoScreenshotsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Piko", "Screenshots");
                Directory.CreateDirectory(pikoScreenshotsDir);

                string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string filePath = Path.Combine(pikoScreenshotsDir, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(fileStream);
                }

                if (!_state.ShelfFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                {
                    _state.ShelfFiles.Add(filePath);
                    SaveAppState();
                    UpdateShelfUI();
                    _soundService.PlaySuccessChime();

                    ShowScreenshotNotification(fileName);
                }
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("OnClipboardUpdate-Error", ex);
        }
    }

    private void ShowScreenshotNotification(string fileName)
    {
        if (!_isExpanded)
        {
            TransitionToState(DynamicIslandState.Notification, new IslandNotificationItem
            {
                Icon = "📸",
                Title = "Screenshot Tersimpan",
                Message = $"{fileName} masuk ke Shelf 📎",
                Duration = TimeSpan.FromSeconds(4)
            });
        }
    }

    // Direct Window Drag & Drop (Dynamic Island Eating Feature)
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;

            if (!_isExpanded && _currentState != DynamicIslandState.FileEating)
            {
                _preDragState = _currentState;
                _isDraggingFileOver = true;
                TransitionToState(DynamicIslandState.FileEating);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        if (_isExpanded) return;

        Point pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
        {
            if (_isDraggingFileOver && _currentState == DynamicIslandState.FileEating)
            {
                _isDraggingFileOver = false;
                TransitionToState(_preDragState);
            }
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _isDraggingFileOver = false;
        if (e.Handled) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                int addedCount = 0;
                foreach (var path in files)
                {
                    if (!_state.ShelfFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        _state.ShelfFiles.Add(path);
                        addedCount++;
                    }
                }

                SaveAppState();
                UpdateShelfUI();
                _soundService.PlaySuccessChime();

                if (!_isExpanded)
                {
                    string msg = addedCount > 1
                        ? $"{addedCount} file disimpan ke Shelf! 📎"
                        : $"{Path.GetFileName(files[0])} disimpan ke Shelf! 📎";
                    TransitionToState(DynamicIslandState.Success, ("Nyam!", msg));
                }
            }
            e.Handled = true;
        }
        else if (!_isExpanded && _currentState == DynamicIslandState.FileEating)
        {
            TransitionToState(_preDragState);
        }
    }

    // Shelf Tab Drag & Drop
    private void Shelf_DragEnter(object sender, DragEventArgs e) => SetShelfDrag(e);
    private void Shelf_DragOver(object sender, DragEventArgs e) => SetShelfDrag(e);

    private static void SetShelfDrag(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Shelf_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        int addedCount = 0;
        foreach (var path in paths)
        {
            if (!_state.ShelfFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _state.ShelfFiles.Add(path);
                addedCount++;
            }
        }
        SaveAppState();
        UpdateShelfUI();
        _soundService.PlayGentleTap();
        e.Handled = true;
    }

    private void Shelf_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept if an inline edit box (like rename) is currently focused
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+A: Select all shelf files
            _selectedShelfFilePaths.Clear();
            foreach (var f in _state.ShelfFiles)
            {
                _selectedShelfFilePaths.Add(f);
            }
            if (_state.ShelfFiles.Count > 0 && string.IsNullOrEmpty(_shelfAnchorPath))
            {
                _shelfAnchorPath = _state.ShelfFiles[0];
            }
            RefreshShelfSelectionVisuals();
            UpdateShelfHintText();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedShelfFilePaths.Count > 0)
            {
                RemoveSelectedShelfFiles();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (_selectedShelfFilePaths.Count > 0)
            {
                _selectedShelfFilePaths.Clear();
                _shelfAnchorPath = null;
                RefreshShelfSelectionVisuals();
                UpdateShelfHintText();
                e.Handled = true;
            }
        }
    }

    private void RemoveSelectedShelfFiles()
    {
        if (_selectedShelfFilePaths.Count == 0) return;

        var toRemove = _selectedShelfFilePaths.ToList();
        foreach (var path in toRemove)
        {
            _state.ShelfFiles.Remove(path);
        }
        _selectedShelfFilePaths.Clear();
        _shelfAnchorPath = null;
        SaveAppState();
        UpdateShelfUI();
        _soundService.PlayGentleTap();
    }

    private void ShelfDropZone_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ShelfDropZone.Focus();
        if (e.OriginalSource == ShelfDropZone || e.OriginalSource is ScrollViewer || e.OriginalSource == ShelfItemsPanel)
        {
            _selectedShelfFilePaths.Clear();
            _shelfAnchorPath = null;
            RefreshShelfSelectionVisuals();
            UpdateShelfHintText();
        }
    }

    private void HandleShiftSelect(string path)
    {
        if (_state.ShelfFiles.Count == 0) return;

        if (string.IsNullOrEmpty(_shelfAnchorPath) || !_state.ShelfFiles.Contains(_shelfAnchorPath, StringComparer.OrdinalIgnoreCase))
        {
            _shelfAnchorPath = _state.ShelfFiles[0];
        }

        int anchorIdx = _state.ShelfFiles.FindIndex(p => string.Equals(p, _shelfAnchorPath, StringComparison.OrdinalIgnoreCase));
        int targetIdx = _state.ShelfFiles.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

        if (anchorIdx < 0) anchorIdx = 0;
        if (targetIdx < 0) targetIdx = 0;

        int start = Math.Min(anchorIdx, targetIdx);
        int end = Math.Max(anchorIdx, targetIdx);

        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!isCtrl)
        {
            _selectedShelfFilePaths.Clear();
        }

        for (int i = start; i <= end; i++)
        {
            _selectedShelfFilePaths.Add(_state.ShelfFiles[i]);
        }

        // Anchor is retained for further range extensions (matches Windows Explorer)
        RefreshShelfSelectionVisuals();
        UpdateShelfHintText();
    }

    private void HandleCtrlSelect(string path)
    {
        if (_selectedShelfFilePaths.Contains(path))
        {
            _selectedShelfFilePaths.Remove(path);
            if (string.Equals(_shelfAnchorPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _shelfAnchorPath = _selectedShelfFilePaths.FirstOrDefault();
            }
        }
        else
        {
            _selectedShelfFilePaths.Add(path);
            _shelfAnchorPath = path;
        }
        RefreshShelfSelectionVisuals();
        UpdateShelfHintText();
    }

    private void SelectSingleShelfItem(string path)
    {
        _selectedShelfFilePaths.Clear();
        _selectedShelfFilePaths.Add(path);
        _shelfAnchorPath = path;
        RefreshShelfSelectionVisuals();
        UpdateShelfHintText();
    }

    private void SelectShelfItem(string? path)
    {
        if (path == null)
        {
            _selectedShelfFilePaths.Clear();
            _shelfAnchorPath = null;
        }
        else
        {
            SelectSingleShelfItem(path);
        }
        RefreshShelfSelectionVisuals();
        UpdateShelfHintText();
    }

    private void UpdateShelfHintText()
    {
        if (ShelfHintText == null) return;
        if (_state.ShelfFiles.Count == 0)
        {
            ShelfHintText.Text = "Tarik dan lepas file dari Windows Explorer ke area ini.";
            return;
        }

        if (_selectedShelfFilePaths.Count > 1)
        {
            ShelfHintText.Text = $"{_selectedShelfFilePaths.Count} dari {_state.ShelfFiles.Count} file dipilih · Drag keluar 📤 · Del untuk hapus";
        }
        else if (_selectedShelfFilePaths.Count == 1)
        {
            ShelfHintText.Text = $"1 file dipilih · Shift/Ctrl+klik untuk pilih beberapa · Drag keluar 📤";
        }
        else
        {
            ShelfHintText.Text = $"{_state.ShelfFiles.Count} file terparkir · Shift/Ctrl+klik untuk multi-pilih · Drag keluar 📤";
        }
    }

    private void RefreshShelfSelectionVisuals()
    {
        bool isDark = _state.DarkMode;
        Brush normalBg = isDark
            ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        Brush selectedBg = isDark
            ? new SolidColorBrush(Color.FromArgb(90, 139, 92, 246))
            : new SolidColorBrush(Color.FromArgb(50, 139, 92, 246));
        Brush normalBorder = (Brush)FindResource("LavenderBrush");
        Brush selectedBorder = (Brush)FindResource("PrimaryVioletBrush");
        Brush normalText = isDark
            ? (Brush)FindResource("Dark_TextPrimaryBrush")
            : (Brush)FindResource("MainTextBrush");
        Brush selectedText = (Brush)FindResource("PrimaryVioletBrush");
        Brush normalMuted = (Brush)FindResource("MutedBrush");

        foreach (var child in ShelfItemsPanel.Children)
        {
            if (child is Border card && card.Tag is string itemPath)
            {
                bool isSelected = _selectedShelfFilePaths.Contains(itemPath);
                card.Background = isSelected ? selectedBg : normalBg;
                card.BorderBrush = isSelected ? selectedBorder : normalBorder;
                card.BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(1);

                if (card.Child is DockPanel dock)
                {
                    foreach (var el in dock.Children)
                    {
                        if (el is FrameworkElement fe)
                        {
                            if (fe.Name == "SelectionIndicator")
                            {
                                fe.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                            }
                            else if (fe.Name == "FileNameBlock" && fe is TextBlock tb)
                            {
                                tb.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
                                tb.Foreground = isSelected ? selectedText : normalText;
                            }
                            else if (fe.Name == "DragHandleBlock" && fe is TextBlock handleTb)
                            {
                                handleTb.Foreground = isSelected ? selectedText : normalMuted;
                            }
                        }
                    }
                }
            }
        }
    }

    private void UpdateShelfUI()
    {
        ShelfItemsPanel.Children.Clear();

        // Prune deleted or missing references from selection
        _selectedShelfFilePaths.RemoveWhere(p => !_state.ShelfFiles.Contains(p, StringComparer.OrdinalIgnoreCase));
        if (_shelfAnchorPath != null && !_state.ShelfFiles.Contains(_shelfAnchorPath, StringComparer.OrdinalIgnoreCase))
        {
            _shelfAnchorPath = _selectedShelfFilePaths.FirstOrDefault();
        }

        if (DragAllShelfButton != null)
        {
            DragAllShelfButton.IsEnabled = _state.ShelfFiles.Count > 0;
        }
        ClearShelfButton.IsEnabled = _state.ShelfFiles.Count > 0;

        UpdateShelfHintText();
        if (_state.ShelfFiles.Count == 0)
        {
            return;
        }

        bool isDark = _state.DarkMode;
        Brush normalBg = isDark
            ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        Brush selectedBg = isDark
            ? new SolidColorBrush(Color.FromArgb(90, 139, 92, 246))
            : new SolidColorBrush(Color.FromArgb(50, 139, 92, 246));
        Brush normalBorder = (Brush)FindResource("LavenderBrush");
        Brush selectedBorder = (Brush)FindResource("PrimaryVioletBrush");
        Brush textColor = isDark
            ? (Brush)FindResource("Dark_TextPrimaryBrush")
            : (Brush)FindResource("MainTextBrush");
        Brush selectedText = (Brush)FindResource("PrimaryVioletBrush");
        Brush normalMuted = (Brush)FindResource("MutedBrush");

        foreach (var path in _state.ShelfFiles.ToList())
        {
            var isRenamingThis = string.Equals(_renamingFilePath, path, StringComparison.OrdinalIgnoreCase);
            bool isSelected = _selectedShelfFilePaths.Contains(path);

            var itemBorder = new Border
            {
                Tag = path,
                Background = isSelected ? selectedBg : normalBg,
                BorderBrush = isRenamingThis
                    ? (Brush)FindResource("AccentGradientBrush")
                    : (isSelected ? selectedBorder : normalBorder),
                BorderThickness = isSelected ? new Thickness(1.5) : new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 3, 0, 3)
            };

            var dock = new DockPanel { LastChildFill = true };

            if (isRenamingThis)
            {
                // Inline rename editor row
                var editBox = new TextBox
                {
                    Text = Path.GetFileName(path),
                    Style = (Style)FindResource("PikoTextBox"),
                    Height = 28,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 6, 0)
                };

                var saveBtn = new Button
                {
                    Content = "✓",
                    Style = (Style)FindResource("PikoButton"),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = "Simpan Nama Baru (Enter)",
                    VerticalAlignment = VerticalAlignment.Center
                };

                var cancelBtn = new Button
                {
                    Content = "✕",
                    Style = (Style)FindResource("PikoButton"),
                    Padding = new Thickness(8, 2, 8, 2),
                    ToolTip = "Batal (Esc)",
                    VerticalAlignment = VerticalAlignment.Center
                };

                Action commit = () => RenameShelfFile(path, editBox.Text);
                Action cancel = () =>
                {
                    _renamingFilePath = null;
                    UpdateShelfUI();
                };

                saveBtn.Click += (_, _) => commit();
                cancelBtn.Click += (_, _) => cancel();

                editBox.KeyDown += (_, ke) =>
                {
                    if (ke.Key == Key.Enter)
                    {
                        ke.Handled = true;
                        commit();
                    }
                    else if (ke.Key == Key.Escape)
                    {
                        ke.Handled = true;
                        cancel();
                    }
                };

                editBox.Loaded += (_, _) =>
                {
                    editBox.Focus();
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(path);
                    editBox.Select(0, nameWithoutExt.Length);
                };

                DockPanel.SetDock(cancelBtn, Dock.Right);
                dock.Children.Add(cancelBtn);
                DockPanel.SetDock(saveBtn, Dock.Right);
                dock.Children.Add(saveBtn);
                dock.Children.Add(editBox);
            }
            else
            {
                // Normal item row
                Point? itemDragStart = null;
                bool itemIsDragging = false;
                bool potentialSingleSelect = false;

                itemBorder.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null) return;

                    // Double click: open file directly like Windows Explorer
                    if (e.ClickCount >= 2)
                    {
                        itemDragStart = null;
                        itemIsDragging = false;
                        potentialSingleSelect = false;
                        if (itemBorder.IsMouseCaptured) itemBorder.ReleaseMouseCapture();

                        try
                        {
                            if (File.Exists(path) || Directory.Exists(path))
                            {
                                _soundService.PlayGentleTap();
                                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                            }
                        }
                        catch
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                            }
                            catch { }
                        }
                        e.Handled = true;
                        return;
                    }

                    ShelfDropZone.Focus();

                    bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                    if (isShift)
                    {
                        HandleShiftSelect(path);
                        potentialSingleSelect = false;
                    }
                    else if (isCtrl)
                    {
                        HandleCtrlSelect(path);
                        potentialSingleSelect = false;
                    }
                    else
                    {
                        // If already part of multi-selection, don't collapse immediately so dragging multiple files works
                        if (_selectedShelfFilePaths.Contains(path) && _selectedShelfFilePaths.Count > 1)
                        {
                            potentialSingleSelect = true;
                        }
                        else
                        {
                            SelectSingleShelfItem(path);
                            potentialSingleSelect = false;
                        }
                    }

                    itemDragStart = e.GetPosition(this);
                    itemIsDragging = false;
                    itemBorder.CaptureMouse();
                };

                itemBorder.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed && itemDragStart.HasValue && !itemIsDragging)
                    {
                        Point cur = e.GetPosition(this);
                        Vector diff = itemDragStart.Value - cur;
                        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
                        {
                            itemIsDragging = true;
                            potentialSingleSelect = false;
                            itemDragStart = null;
                            if (itemBorder.IsMouseCaptured) itemBorder.ReleaseMouseCapture();

                            string[] filesToDrag;
                            if (_selectedShelfFilePaths.Contains(path) && _selectedShelfFilePaths.Count > 1)
                            {
                                // Drag all valid selected files
                                filesToDrag = _state.ShelfFiles
                                    .Where(p => _selectedShelfFilePaths.Contains(p))
                                    .Select(Path.GetFullPath)
                                    .Where(p => File.Exists(p) || Directory.Exists(p))
                                    .ToArray();
                            }
                            else
                            {
                                string fullPath = Path.GetFullPath(path);
                                filesToDrag = (File.Exists(fullPath) || Directory.Exists(fullPath)) ? new[] { fullPath } : Array.Empty<string>();
                            }

                            if (filesToDrag.Length > 0)
                            {
                                try
                                {
                                    var data = new DataObject();
                                    var sc = new System.Collections.Specialized.StringCollection();
                                    foreach (var f in filesToDrag) sc.Add(f);
                                    data.SetFileDropList(sc);
                                    data.SetData(DataFormats.FileDrop, filesToDrag);

                                    _soundService.PlayGentleTap();
                                    PerformShelfDragDrop(this, data);
                                }
                                catch (Exception ex)
                                {
                                    LogDiagnostic("ItemDragOut-Error", ex);
                                }
                            }
                        }
                    }
                };

                itemBorder.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    itemDragStart = null;
                    if (itemBorder.IsMouseCaptured) itemBorder.ReleaseMouseCapture();

                    // If released without dragging and potentialSingleSelect was primed, collapse selection to this single item
                    if (potentialSingleSelect && !itemIsDragging)
                    {
                        potentialSingleSelect = false;
                        SelectSingleShelfItem(path);
                    }
                };

                var removeBtn = new Button
                {
                    Content = "×",
                    Style = (Style)FindResource("PikoButton"),
                    Padding = new Thickness(6, 1, 6, 1),
                    ToolTip = "Hapus dari Shelf",
                    VerticalAlignment = VerticalAlignment.Center
                };
                removeBtn.Click += (_, _) =>
                {
                    _selectedShelfFilePaths.Remove(path);
                    if (string.Equals(_shelfAnchorPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        _shelfAnchorPath = _selectedShelfFilePaths.FirstOrDefault();
                    }
                    _state.ShelfFiles.Remove(path);
                    SaveAppState();
                    UpdateShelfUI();
                };

                var renameBtn = new Button
                {
                    Content = "✎",
                    Style = (Style)FindResource("PikoButton"),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = "Ganti Nama File",
                    VerticalAlignment = VerticalAlignment.Center
                };
                renameBtn.Click += (_, _) =>
                {
                    _renamingFilePath = path;
                    UpdateShelfUI();
                };

                var selectIndicator = new Border
                {
                    Name = "SelectionIndicator",
                    Width = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = (Brush)FindResource("AccentGradientBrush"),
                    Margin = new Thickness(0, 2, 6, 2),
                    Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
                };

                var dragHandle = new TextBlock
                {
                    Name = "DragHandleBlock",
                    Text = "⠿",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = isSelected ? (Brush)FindResource("PrimaryVioletBrush") : normalMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Drag file ini keluar ke Windows Explorer atau aplikasi lain 📤"
                };

                string fileIcon = GetFileIcon(path);
                var iconBlock = new TextBlock
                {
                    Text = fileIcon,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand
                };

                var fileBtn = new TextBlock
                {
                    Name = "FileNameBlock",
                    Text = Path.GetFileName(path),
                    ToolTip = $"{path}\n(Klik 1x untuk pilih · Shift/Ctrl+klik multi-pilih · Klik 2x buka · Drag keluar · Del hapus)",
                    Foreground = isSelected ? selectedText : textColor,
                    FontSize = 11.5,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Cursor = Cursors.Hand
                };

                DockPanel.SetDock(removeBtn, Dock.Right);
                dock.Children.Add(removeBtn);
                DockPanel.SetDock(renameBtn, Dock.Right);
                dock.Children.Add(renameBtn);
                DockPanel.SetDock(selectIndicator, Dock.Left);
                dock.Children.Add(selectIndicator);
                DockPanel.SetDock(dragHandle, Dock.Left);
                dock.Children.Add(dragHandle);
                DockPanel.SetDock(iconBlock, Dock.Left);
                dock.Children.Add(iconBlock);
                dock.Children.Add(fileBtn);
            }

            itemBorder.Child = dock;
            ShelfItemsPanel.Children.Add(itemBorder);
        }
    }

    private void PerformShelfDragDrop(DependencyObject dragSource, DataObject data)
    {
        bool collapsedDuringDrag = false;

        QueryContinueDragEventHandler queryHandler = (s, qe) =>
        {
            if (!collapsedDuringDrag)
            {
                if (GetCursorPos(out Win32Point pt))
                {
                    try
                    {
                        double curW = ActualWidth > 0 ? ActualWidth : Width;
                        double curH = ActualHeight > 0 ? ActualHeight : Height;
                        Point tl = PointToScreen(new Point(0, 0));
                        Point br = PointToScreen(new Point(curW, curH));

                        double minX = Math.Min(tl.X, br.X);
                        double maxX = Math.Max(tl.X, br.X);
                        double minY = Math.Min(tl.Y, br.Y);
                        double maxY = Math.Max(tl.Y, br.Y);

                        if (pt.X < minX || pt.X > maxX || pt.Y < minY || pt.Y > maxY)
                        {
                            collapsedDuringDrag = true;
                            Dispatcher.BeginInvoke(() =>
                            {
                                SetExpanded(false, isUserPin: false);
                            });
                        }
                    }
                    catch { }
                }
            }
        };

        DragDrop.AddQueryContinueDragHandler(dragSource, queryHandler);

        try
        {
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

            if (!collapsedDuringDrag)
            {
                CheckAndAutoCollapseAfterDrag();
            }
        }
        finally
        {
            DragDrop.RemoveQueryContinueDragHandler(dragSource, queryHandler);
        }
    }

    private void CheckAndAutoCollapseAfterDrag()
    {
        try
        {
            bool isOutside = true;
            if (GetCursorPos(out Win32Point pt))
            {
                double curW = ActualWidth > 0 ? ActualWidth : Width;
                double curH = ActualHeight > 0 ? ActualHeight : Height;
                Point tl = PointToScreen(new Point(0, 0));
                Point br = PointToScreen(new Point(curW, curH));

                double minX = Math.Min(tl.X, br.X);
                double maxX = Math.Max(tl.X, br.X);
                double minY = Math.Min(tl.Y, br.Y);
                double maxY = Math.Max(tl.Y, br.Y);

                if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY)
                {
                    isOutside = false;
                }
            }

            if (isOutside)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    SetExpanded(false, isUserPin: false);
                });
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic("CheckAndAutoCollapseAfterDrag-Error", ex);
            Dispatcher.BeginInvoke(() =>
            {
                SetExpanded(false, isUserPin: false);
            });
        }
    }

    private Point? _dragAllStart;
    private bool _dragAllActive;

    private void DragAllShelf_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragAllStart = e.GetPosition(this);
        _dragAllActive = false;
        DragAllShelfButton.CaptureMouse();
    }

    private void DragAllShelf_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _dragAllStart.HasValue && !_dragAllActive)
        {
            Point cur = e.GetPosition(this);
            Vector diff = _dragAllStart.Value - cur;
            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                _dragAllActive = true;
                _dragAllStart = null;
                if (DragAllShelfButton.IsMouseCaptured) DragAllShelfButton.ReleaseMouseCapture();

                var existingFiles = _state.ShelfFiles
                    .Select(Path.GetFullPath)
                    .Where(p => File.Exists(p) || Directory.Exists(p))
                    .ToArray();

                if (existingFiles.Length > 0)
                {
                    try
                    {
                        var data = new DataObject();
                        var sc = new System.Collections.Specialized.StringCollection();
                        foreach (var f in existingFiles) sc.Add(f);
                        data.SetFileDropList(sc);
                        data.SetData(DataFormats.FileDrop, existingFiles);

                        _soundService.PlayGentleTap();
                        PerformShelfDragDrop(this, data);
                    }
                    catch (Exception ex)
                    {
                        LogDiagnostic("DragAll-Error", ex);
                    }
                }
            }
        }
    }

    private void DragAllShelf_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragAllStart = null;
        if (DragAllShelfButton.IsMouseCaptured) DragAllShelfButton.ReleaseMouseCapture();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = child;
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void RenameShelfFile(string oldPath, string newFileName)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
        {
            _renamingFilePath = null;
            UpdateShelfUI();
            return;
        }

        newFileName = newFileName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            newFileName = newFileName.Replace(c.ToString(), "");
        }

        if (string.IsNullOrWhiteSpace(newFileName))
        {
            MessageBox.Show("Nama file tidak boleh mengandung karakter khusus.", "Ganti Nama", MessageBoxButton.OK, MessageBoxImage.Warning);
            _renamingFilePath = null;
            UpdateShelfUI();
            return;
        }

        try
        {
            if (File.Exists(oldPath))
            {
                string? dir = Path.GetDirectoryName(oldPath);
                if (dir == null) return;

                string oldExt = Path.GetExtension(oldPath);
                if (!string.IsNullOrEmpty(oldExt) && !Path.HasExtension(newFileName))
                {
                    newFileName += oldExt;
                }

                string newPath = Path.Combine(dir, newFileName);
                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newPath))
                    {
                        MessageBox.Show($"File dengan nama '{newFileName}' sudah ada di folder ini.", "Ganti Nama", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    File.Move(oldPath, newPath);

                    int idx = _state.ShelfFiles.IndexOf(oldPath);
                    if (idx >= 0) _state.ShelfFiles[idx] = newPath;
                    else _state.ShelfFiles.Add(newPath);

                    if (_selectedShelfFilePaths.Contains(oldPath))
                    {
                        _selectedShelfFilePaths.Remove(oldPath);
                        _selectedShelfFilePaths.Add(newPath);
                    }
                    if (string.Equals(_shelfAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _shelfAnchorPath = newPath;
                    }

                    SaveAppState();
                    _soundService.PlayGentleTap();
                }
            }
            else if (Directory.Exists(oldPath))
            {
                string? parent = Directory.GetParent(oldPath)?.FullName;
                if (parent == null) return;

                string newPath = Path.Combine(parent, newFileName);
                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(newPath))
                    {
                        MessageBox.Show($"Folder dengan nama '{newFileName}' sudah ada.", "Ganti Nama", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Directory.Move(oldPath, newPath);

                    int idx = _state.ShelfFiles.IndexOf(oldPath);
                    if (idx >= 0) _state.ShelfFiles[idx] = newPath;
                    else _state.ShelfFiles.Add(newPath);

                    if (_selectedShelfFilePaths.Contains(oldPath))
                    {
                        _selectedShelfFilePaths.Remove(oldPath);
                        _selectedShelfFilePaths.Add(newPath);
                    }
                    if (string.Equals(_shelfAnchorPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _shelfAnchorPath = newPath;
                    }

                    SaveAppState();
                    _soundService.PlayGentleTap();
                }
            }
            else
            {
                MessageBox.Show("File tidak ditemukan di disk.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal mengubah nama file: {ex.Message}", "Ganti Nama Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _renamingFilePath = null;
            UpdateShelfUI();
        }
    }

    private static string GetFileIcon(string path)
    {
        if (Directory.Exists(path)) return "📁";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "📸",
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "🎵",
            ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            ".pdf" => "📕",
            ".doc" or ".docx" or ".txt" or ".md" => "📄",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
            ".exe" or ".msi" => "⚙️",
            _ => "📎"
        };
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
        _selectedShelfFilePaths.Clear();
        _shelfAnchorPath = null;
        _state.ShelfFiles.Clear();
        SaveAppState();
        UpdateShelfUI();
    }

    // ---------------------------------------------------------
    // PERSISTENCE & QUIT
    // ---------------------------------------------------------

    private void LoadAppState()
    {
        _state = _persistenceService.Load();
        _soundService.SoundEnabled = _state.SoundEnabled;
        _soundService.DoNotDisturb = _state.DoNotDisturb;

        SoundToggleButton.Content = _state.SoundEnabled ? "♫ Sound On" : "♫ Sound Off";
        SettingsSoundBtn.Content = _state.SoundEnabled ? "Enabled" : "Muted";

        DndMiniButton.Content = _state.DoNotDisturb ? "🔕" : "🔔";
        DndToggleButton.Content = _state.DoNotDisturb ? "🔕 DND On" : "🔔 DND Off";
        SettingsDndBtn.Content = _state.DoNotDisturb ? "Active" : "Disabled";

        SettingsReducedMotionBtn.Content = _state.ReducedMotion ? "On" : "Off";
        SettingsDarkModeBtn.Content = _state.DarkMode ? "On" : "Off";
        UndoMoveButton.IsEnabled = _state.OperationJournals.Count > 0;

        _dockEdge = _state.DockEdge switch
        {
            "Left" => ScreenDockEdge.Left,
            "Right" => ScreenDockEdge.Right,
            _ => ScreenDockEdge.Top
        };
        _customLeft = _state.CustomLeft;
        _customTop = _state.CustomTop;
        _isCustomPosition = _state.IsCustomPosition;

        if (GoogleClientIdInput != null) GoogleClientIdInput.Text = _state.GoogleClientId ?? "";
        if (GoogleClientSecretInput != null) GoogleClientSecretInput.Text = _state.GoogleClientSecret ?? "";
    }

    private void SaveAppState()
    {
        _state.DockEdge = _dockEdge.ToString();
        _state.CustomLeft = _customLeft;
        _state.CustomTop = _customTop;
        _state.IsCustomPosition = _isCustomPosition;
        _persistenceService.Save(_state);
    }

    private void QuitMiniButton_Click(object sender, RoutedEventArgs e)
    {
        SetExpanded(false);
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        SaveAppState();
        Close();
    }
}
