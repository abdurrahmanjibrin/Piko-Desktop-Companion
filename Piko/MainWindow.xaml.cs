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
    MusicActive,
    MediaPaused,
    AiWorking,
    Notification,
    Success,
    Expanded,
    Pinned
}

public enum NotificationPriority
{
    Routine = 0,
    Timer = 1,
    Ai = 2
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
    private const double MusicActiveWidth = 250;
    private const double MusicActiveHeight = 50;
    private const double MusicHoverWidth = 280;
    private const double SleepyWidth = 96;
    private const double SleepyHeight = 36;
    private const double AiWorkingWidth = 310;
    private const double AiWorkingHeight = 44;
    private const double SuccessWidth = 330;
    private const double SuccessHeight = 44;
    private const double NotificationWidth = 320;
    private const double NotificationHeight = 44;
    private const double ExpandedWidth = 720;
    private const double ExpandedHeight = 510;

    // Services
    private readonly PikoStateManager _stateManager = new();
    private readonly SoundService _soundService = new();
    private readonly LocalBridgeService _bridgeService = new();
    private readonly StatePersistenceService _persistenceService = new();
    private readonly StorageScannerService _storageScanner = new();
    private readonly StorageOrganizerService _storageOrganizer = new();

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

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

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
        _diagnosticCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _diagnosticCheckTimer.Tick += DiagnosticCheckTimer_Tick;
        _diagnosticCheckTimer.Start();

        // Subscribe to State Manager events
        _stateManager.EmotionChanged += StateManager_EmotionChanged;
        _stateManager.StatusChanged += StateManager_StatusChanged;
        _stateManager.NotificationChanged += StateManager_NotificationChanged;

        _commandTimeoutTimer.Tick += CommandTimeoutTimer_Tick;

        // Subscribe to Local Bridge events
        _bridgeService.MessageReceived += BridgeService_MessageReceived;
        _bridgeService.StatusChanged += BridgeService_StatusChanged;
        _bridgeService.DiagnosticLogged += msg => LogDiagnostic(msg);
        _bridgeService.DiagnosticRecoverRequested += () => Dispatcher.Invoke(() => TriggerDiagnosticRecovery(true));
        _bridgeService.ToggleVisualRequested += () => Dispatcher.Invoke(ToggleVisualDiagnosticsMode);
        _bridgeService.ExpandRequested += () => Dispatcher.Invoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            PositionNotch(forceTopmost: true);
            SetExpanded(true, isUserPin: true);
            Activate();
            Topmost = true;
        });
        _bridgeService.CollapseRequested += () => Dispatcher.Invoke(() => SetExpanded(false, isUserPin: false));
        _bridgeService.SimulateMusicRequested += () => Dispatcher.Invoke(() =>
        {
            _demoPlaying = true;
            PlayPauseButton.Content = "⏸ Pause Demo";
            HomePlayPauseBtn.Content = "⏸ Pause Demo";
            ShowMusicActivity();
        });
        _bridgeService.MediaPlayingWithPayloadReceived += (source, title, payload) => Dispatcher.Invoke(() => HandleMediaPlayingWithPayload(source, title, payload));
        _bridgeService.MediaPlayingReceived += (source, title) => Dispatcher.Invoke(() => HandleMediaPlaying(source, title));
        _bridgeService.MediaMetadataReceived += (payload) => Dispatcher.Invoke(() => HandleMediaMetadata(payload));
        _bridgeService.MediaPausedWithPayloadReceived += (source, payload) => Dispatcher.Invoke(() => HandleMediaPausedWithPayload(source, payload));
        _bridgeService.MediaPausedReceived += source => Dispatcher.Invoke(() => HandleMediaPaused(source));
        _bridgeService.MediaStoppedWithPayloadReceived += (source, payload) => Dispatcher.Invoke(() => HandleMediaStoppedWithPayload(source, payload));
        _bridgeService.MediaStoppedReceived += source => Dispatcher.Invoke(() => HandleMediaStopped(source));
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
            PositionNotch(forceTopmost: true);
            _bridgeService.Start();

            UpdateWaterUI();
            UpdateTodoList();
            UpdateShelfUI();
            UpdateDriveStatsUI();
            RenderRecentEvents();
            ApplyEmotionVisuals(PikoEmotion.Idle);
            ApplyTheme();

            // Re-assert positioning and topmost on Dispatcher loaded priority
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                PositionNotch(forceTopmost: true);
                if (Environment.GetCommandLineArgs().Contains("--expand"))
                {
                    SetExpanded(true, isUserPin: true);
                    Activate();
                }
                LogDiagnostic("Window_Loaded-PostDispatcherLoaded");
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
            TransitionToState(DynamicIslandState.Idle);
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
                LogDiagnostic("DiagnosticCheckTimer: piko_recover.flag detected, triggering recovery");
                TriggerDiagnosticRecovery(toggleVisual: true);
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
                DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused => MusicActiveWidth,
                DynamicIslandState.AiWorking => AiWorkingWidth,
                DynamicIslandState.Success => SuccessWidth,
                DynamicIslandState.Notification => NotificationWidth,
                DynamicIslandState.Expanded or DynamicIslandState.Pinned => ExpandedWidth,
                _ => IdleWidth
            };

            var curHeight = _currentState switch
            {
                DynamicIslandState.Expanded or DynamicIslandState.Pinned => ExpandedHeight,
                DynamicIslandState.MusicActive or DynamicIslandState.MediaPaused => MusicActiveHeight,
                DynamicIslandState.AiWorking => AiWorkingHeight,
                DynamicIslandState.Success => SuccessHeight,
                DynamicIslandState.Notification => NotificationHeight,
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
                    MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                    MascotCapsule.VerticalAlignment = VerticalAlignment.Center;
                    MascotCapsule.Margin = new Thickness(8, 0, 0, 0);
                    MascotCapsule.Width = 44;
                    MascotCapsule.Height = 28;
                }
                else
                {
                    MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
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
        _eyeController?.ResetTracking();

        if (_currentState == DynamicIslandState.Hover)
        {
            TransitionToState(_isMediaPlaying || _demoPlaying ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
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
            TransitionToState(DynamicIslandState.Idle);
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
            if (_isMediaPlaying || _demoPlaying)
            {
                TransitionToState(DynamicIslandState.MusicActive);
            }
            else
            {
                TransitionToState(DynamicIslandState.Idle);
            }
        }
        else
        {
            // Currently collapsed (Idle, Sleepy, MusicActive, Hover, etc.) -> EXPAND
            _isPinned = true;
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
            TransitionToState((_isMediaPlaying || _demoPlaying) ? DynamicIslandState.MusicActive : DynamicIslandState.Idle);
        }
    }

    private void DismissActivity_Click(object sender, RoutedEventArgs e)
    {
        DismissCurrentActivity();
    }

    private void SuccessPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TabAi.IsChecked = true;
        UpdateTabPanels();
        TransitionToState(DynamicIslandState.Pinned);
    }

    private void MusicPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TabMusic.IsChecked = true;
        UpdateTabPanels();
        TransitionToState(DynamicIslandState.Pinned);
    }

    private void ActivityAutoCollapseTimer_Tick(object? sender, EventArgs e)
    {
        _activityAutoCollapseTimer.Stop();
        if (_notificationQueue.Count > 0)
        {
            var next = _notificationQueue.Dequeue();
            TransitionToState(DynamicIslandState.Notification, next);
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

        if (_currentState is not DynamicIslandState.Expanded and not DynamicIslandState.Pinned)
        {
            TransitionToState(DynamicIslandState.MusicActive);
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
                targetWidth = AiWorkingWidth;
                targetHeight = AiWorkingHeight;
                BrandPanel.Visibility = Visibility.Collapsed;
                MascotCapsule.HorizontalAlignment = HorizontalAlignment.Left;
                MascotCapsule.Margin = new Thickness(4, 0, 0, 0);
                MusicActiveCompactPanel.Visibility = Visibility.Collapsed;
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Visible;
                SuccessPanel.Visibility = Visibility.Collapsed;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                if (payload is string s) AiWorkingText.Text = s;
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
                ActivityPanel.Visibility = Visibility.Visible;
                ActivityPanel.Opacity = 1.0;
                AiWorkingPanel.Visibility = Visibility.Collapsed;
                SuccessPanel.Visibility = Visibility.Visible;
                MusicActivePanel.Visibility = Visibility.Collapsed;
                NotificationPanel.Visibility = Visibility.Collapsed;
                HeaderControls.Visibility = Visibility.Collapsed;
                ExpandedContent.Visibility = Visibility.Collapsed;
                ExpandedContent.Opacity = 0.0;
                IdleMusicDot.Visibility = Visibility.Collapsed;
                Shell.CornerRadius = new CornerRadius(0, 0, 18, 18);
                _equalizerTimer.Stop();
                _isPinned = false;
                _panelState = NotchPanelState.Collapsed;
                if (payload is string sub) SuccessSubtext.Text = sub;
                _stateManager.SetEmotion(PikoEmotion.Success);
                _eyeController?.ApplyEmotion(PikoEmotion.Success, _state.ReducedMotion);
                _eyeController?.SetHeadsetVisible(false);
                _soundService.PlaySuccessChime();
                _activityAutoCollapseTimer.Interval = TimeSpan.FromSeconds(6);
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
                DynamicIslandState.Notification or DynamicIslandState.Success => TimeSpan.FromMilliseconds(300),
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
            DynamicIslandState.Notification or DynamicIslandState.Success =>
                new BackEase { Amplitude = 0.12, EasingMode = EasingMode.EaseOut },
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
                AIStatusText.Text = "Buka ChatGPT di Chrome/Edge. Saat generasi selesai, Piko akan memberi tahu seketika ✨";
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
                    StartAiWorking(msg.Title ?? "Piko sedang berpikir...");
                    break;

                case BridgeMessage.TypeComplete:
                case BridgeMessage.TypeGenerationCompleted:
                    var source = string.IsNullOrWhiteSpace(msg.Source) ? "ChatGPT" : msg.Source;
                    var title = string.IsNullOrWhiteSpace(msg.Title) ? "Jawaban sudah siap ✨" : msg.Title;
                    CompleteAiSuccess(source, title);
                    break;

                case BridgeMessage.TypeCancel:
                case BridgeMessage.TypeGenerationCancelled:
                    CancelAiWorking("Generasi AI dihentikan.");
                    break;
            }
        });
    }

    private void StartAiWorking(string message)
    {
        AIStatusBadgeText.Text = "WORKING";
        AIStatusBadge.Background = new SolidColorBrush(Color.FromRgb(245, 235, 255));
        AIStatusBadgeText.Foreground = (Brush)FindResource("PrimaryVioletBrush");
        AIStatusText.Text = message;
        _stateManager.SetEmotion(PikoEmotion.Working, null, message);
        TransitionToState(DynamicIslandState.AiWorking, message);
        LogDiagnostic($"StartAiWorking: {message}");
    }

    private void CompleteAiSuccess(string source, string title)
    {
        if (DateTime.Now - _lastAiCompleteTime < TimeSpan.FromSeconds(2.5))
        {
            LogDiagnostic("Debounced duplicate AI complete message");
            return;
        }
        _lastAiCompleteTime = DateTime.Now;

        _stateManager.TriggerNotification($"{source} selesai! ✨", title, source);

        _state.RecentAiEvents.Insert(0, new RecentAiEvent { Title = title, Source = source, Timestamp = DateTime.Now });
        if (_state.RecentAiEvents.Count > 10) _state.RecentAiEvents.RemoveAt(_state.RecentAiEvents.Count - 1);
        SaveAppState();
        RenderRecentEvents();

        AIStatusBadgeText.Text = "DONE ✨";
        AIStatusBadge.Background = new SolidColorBrush(Color.FromRgb(237, 229, 255));
        AIStatusBadgeText.Foreground = (Brush)FindResource("PrimaryVioletBrush");

        TransitionToState(DynamicIslandState.Success, title);
        LogDiagnostic($"CompleteAiSuccess: {source} - {title}");
    }

    private void CancelAiWorking(string reason)
    {
        AIStatusBadgeText.Text = "STOPPED";
        AIStatusText.Text = reason;
        _stateManager.SetEmotion(PikoEmotion.Alert, TimeSpan.FromSeconds(3), reason);
        TriggerIslandNotification(new IslandNotificationItem
        {
            Priority = NotificationPriority.Ai,
            Icon = "🛑",
            Title = "AI Dibatalkan",
            Message = reason,
            Duration = TimeSpan.FromSeconds(3)
        });
        LogDiagnostic($"CancelAiWorking: {reason}");
    }

    private void BridgeService_StatusChanged(string status)
    {
        Dispatcher.Invoke(() => BridgeConnectionHint.Text = status);
    }

    private void TestAlert_Click(object sender, RoutedEventArgs e)
    {
        CompleteAiSuccess("ChatGPT", "Jawaban ChatGPT sudah siap ✨");
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

        // --- Input backgrounds ---
        var inputBg = dark
            ? new SolidColorBrush(Color.FromArgb(0xD0, 0x1A, 0x16, 0x28))
            : new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
        var inputBorder = dark
            ? (Brush)FindResource("Dark_BorderBrush")
            : (Brush)FindResource("LightPurpleBrush");

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

        // Apply to all expanded content panels recursively
        ApplyThemeToPanel(ExpandedContent, textMain, textMuted, sectionColor, cardBg, cardBorder, chipBg, chipBorder, inputBg, inputBorder, tabHeaderBg, progressTrack, aboutBg, dark);

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

                // Chip badges
                if (border.CornerRadius.TopLeft >= 6 && border.CornerRadius.TopLeft <= 12
                    && border.Padding.Left >= 5 && border.Padding.Left <= 10
                    && border.ActualHeight < 30)
                {
                    if (border.Background is SolidColorBrush scb)
                    {
                        var c = scb.Color;
                        // Light chip bg (#F5F0FF range) or dark chip bg (#221D33 range)
                        if ((c.R > 0xE0 && c.G > 0xD0 && c.B > 0xF0) || (c.R < 0x30 && c.G < 0x25))
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
            }
            else if (child is TextBox txb)
            {
                txb.Background = inputBg;
                txb.BorderBrush = inputBorder;
                txb.Foreground = textMain;
            }
            else if (child is ComboBox cb)
            {
                cb.Background = dark ? new SolidColorBrush(Color.FromRgb(0x1A, 0x16, 0x28)) : new SolidColorBrush(Colors.White);
                cb.Foreground = textMain;
                cb.BorderBrush = inputBorder;
            }
            else if (child is RadioButton rb)
            {
                if (rb.IsChecked != true)
                {
                    rb.Foreground = textMuted;
                }
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

    private void UpdateDriveStatsUI()
    {
        var (total, free, used) = StorageScannerService.GetDriveStats("C:\\");
        if (total > 0)
        {
            var freeGb = free / (1024.0 * 1024.0 * 1024.0);
            var totalGb = total / (1024.0 * 1024.0 * 1024.0);
            var percent = (double)used / total * 100.0;

            DriveStatsText.Text = $"Drive C: {freeGb:F1} GB free of {totalGb:F1} GB";
            DriveProgressBar.Value = percent;
        }
    }

    private string GetSelectedFolderPath()
    {
        var comboIndex = StorageFolderCombo.SelectedIndex;
        var folder = comboIndex switch
        {
            1 => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            2 => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            3 => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            4 => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        return folder;
    }

    private void StorageFolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ScanStatusText.Text = $"Folder siap: {GetSelectedFolderPath()}";
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

        foreach (var cat in result.Categories.Where(c => c.FileCount > 0))
        {
            var card = new Border
            {
                Background = (Brush)FindResource("SoftChipBrush"),
                BorderBrush = (Brush)FindResource("LightPurpleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 8)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{cat.Icon} {cat.Name}",
                Foreground = (Brush)FindResource("MainTextBrush"),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{cat.FileCount} file · {cat.FormattedSize}",
                Foreground = (Brush)FindResource("MutedBrush"),
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
                Foreground = (Brush)FindResource("MainTextBrush"),
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

        foreach (var group in result.DuplicateGroups)
        {
            var groupBorder = new Border
            {
                Background = (Brush)FindResource("SoftChipBrush"),
                BorderBrush = (Brush)FindResource("LightPurpleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"Identik ({group.Files.Count} file · {group.FormattedSize} masing-masing):",
                Foreground = (Brush)FindResource("DeepVioletBrush"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });

            foreach (var f in group.Files)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"• {f.FileName}",
                    ToolTip = f.FilePath,
                    Foreground = (Brush)FindResource("MutedBrush"),
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
        var targetBase = Path.Combine(result.RootPath, "Organized_Piko");
        _currentPlan = await _storageOrganizer.GeneratePlanAsync(result, targetBase);

        if (_currentPlan.Count == 0)
        {
            SmartOrganizeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        SmartOrganizeBorder.Visibility = Visibility.Visible;
        OrganizePlanSummary.Text = $"Usulan Penataan: {_currentPlan.Count} file dapat dirapikan";
        PlanItemsPanel.Children.Clear();

        foreach (var item in _currentPlan)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = item.IsSelected,
                VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += (_, _) => item.IsSelected = true;
            cb.Unchecked += (_, _) => item.IsSelected = false;
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = item.FileName,
                ToolTip = item.SourcePath,
                Foreground = (Brush)FindResource("MainTextBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"➔ {Path.GetFileName(item.TargetFolder)} ({item.FormattedSize})",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 9
            });
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            var statusBlock = new TextBlock
            {
                Text = item.Status.ToString(),
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(statusBlock, 2);
            grid.Children.Add(statusBlock);

            PlanItemsPanel.Children.Add(grid);
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
        else if (TabAi.IsChecked == true) target = PanelAi;
        else if (TabSettings.IsChecked == true) target = PanelSettings;

        SwitchToTabPanel(target);
    }

    private void SwitchToTabPanel(StackPanel newPanel)
    {
        if (newPanel == _currentActivePanel && newPanel.Visibility == Visibility.Visible) return;

        var allPanels = new[] { PanelHome, PanelMusic, PanelFocus, PanelWater, PanelTasks, PanelShelf, PanelStorage, PanelAi, PanelSettings };
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
    // CLOCK & FOCUS TIMER
    // ---------------------------------------------------------

    private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("HH:mm");

    private void FocusStartButton_Click(object sender, RoutedEventArgs e)
    {
        _focusRunning = !_focusRunning;
        if (_focusRunning)
        {
            _focusTimer.Start();
            FocusStartButton.Content = "Pause Focus";
            FocusStatusText.Text = "Fokus pada satu hal. Kamu pasti bisa ✨";
            _stateManager.SetEmotion(PikoEmotion.Listening);
        }
        else
        {
            _focusTimer.Stop();
            FocusStartButton.Content = "Resume Focus";
            FocusStatusText.Text = "Dijeda. Lanjutkan saat siap.";
            _stateManager.SetEmotion(PikoEmotion.Idle);
        }
    }

    private void FocusResetButton_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.Stop();
        _focusRunning = false;
        _focusSecondsRemaining = 25 * 60;
        FocusStartButton.Content = "Start Focus";
        FocusStatusText.Text = "Satu hal pada satu waktu.";
        UpdateFocusDisplay();
        _stateManager.SetEmotion(PikoEmotion.Idle);
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
            _focusTimer.Stop();
            _focusRunning = false;
            FocusStartButton.Content = "Start Focus";
            FocusStatusText.Text = "Sesi selesai! Waktunya istirahat sejenak ☕";
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(6), "Sesi fokus selesai! ✨");
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Timer,
                Icon = "⏱️",
                Title = "Focus Selesai!",
                Message = "Kerja bagus! Waktunya istirahat sejenak ☕",
                Duration = TimeSpan.FromSeconds(6)
            });
        }
    }

    private void UpdateFocusDisplay()
    {
        var minutes = Math.Max(0, _focusSecondsRemaining) / 60;
        var seconds = Math.Max(0, _focusSecondsRemaining) % 60;
        var formatted = $"{minutes:00}:{seconds:00}";
        FocusTimerText.Text = formatted;
        HomeFocusText.Text = formatted;
    }

    // ---------------------------------------------------------
    // WATER REMINDER
    // ---------------------------------------------------------

    private void AddWater_Click(object sender, RoutedEventArgs e)
    {
        if (_state.WaterDate.Date != DateTime.Today)
        {
            _state.WaterDate = DateTime.Today;
            _state.WaterCount = 0;
        }

        _state.WaterCount++;
        SaveAppState();
        UpdateWaterUI();
        _soundService.PlayGentleTap();

        if (_state.WaterCount >= 8)
        {
            _stateManager.SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(5), "Target air tercapai! 💧");
            TriggerIslandNotification(new IslandNotificationItem
            {
                Priority = NotificationPriority.Routine,
                Icon = "💧",
                Title = "Target Air Tercapai!",
                Message = "8 gelas air hari ini berhasil dipenuhi! ✨",
                Duration = TimeSpan.FromSeconds(5)
            });
        }
    }

    private void UpdateWaterUI()
    {
        var text = $"{_state.WaterCount} / 8 glasses";
        WaterCountText.Text = text;
        HomeWaterText.Text = text;

        var pct = Math.Min(100, _state.WaterCount / 8.0 * 100);
        WaterProgress.Value = pct;
        HomeWaterProgress.Value = pct;

        WaterHintText.Text = _state.WaterCount >= 8
            ? "Luar biasa! Target hidrasi harian tercapai ✨"
            : "Pengingat lembut untuk hidrasi, tetap jaga kesehatan.";
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

        if (_state.Todos.Count == 0)
        {
            TodoItemsPanel.Children.Add(new TextBlock
            {
                Text = "Daftar tugas kosong. Nikmati harimu dengan tenang.",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        foreach (var item in _state.Todos.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = item.IsDone,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Tandai selesai"
            };
            cb.Checked += (_, _) => SetTodoDone(item, true);
            cb.Unchecked += (_, _) => SetTodoDone(item, false);
            Grid.SetColumn(cb, 0);
            row.Children.Add(cb);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = item.Title,
                Foreground = item.IsDone ? (Brush)FindResource("MutedBrush") : (Brush)FindResource("MainTextBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = item.IsDone ? TextDecorations.Strikethrough : null
            };
            sp.Children.Add(titleBlock);

            if (item.DueDate.HasValue)
            {
                var isOverdue = !item.IsDone && item.DueDate.Value.Date < DateTime.Today;
                var dueLabel = isOverdue
                    ? $"Terlambat · {item.DueDate.Value:dd MMM}"
                    : item.DueDate.Value.Date == DateTime.Today
                        ? "Hari ini"
                        : item.DueDate.Value.Date == DateTime.Today.AddDays(1)
                            ? "Besok"
                            : $"{item.DueDate.Value:dd MMM}";

                sp.Children.Add(new TextBlock
                {
                    Text = dueLabel,
                    Foreground = isOverdue ? Brushes.OrangeRed : (Brush)FindResource("MutedBrush"),
                    FontSize = 9,
                    Margin = new Thickness(0, 1, 0, 0)
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

            TodoItemsPanel.Children.Add(row);
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
    // FILE SHELF
    // ---------------------------------------------------------

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

        foreach (var path in paths)
        {
            if (!_state.ShelfFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _state.ShelfFiles.Add(path);
            }
        }
        SaveAppState();
        UpdateShelfUI();
        e.Handled = true;
    }

    private void UpdateShelfUI()
    {
        ShelfItemsPanel.Children.Clear();
        ClearShelfButton.IsEnabled = _state.ShelfFiles.Count > 0;

        if (_state.ShelfFiles.Count == 0)
        {
            ShelfHintText.Text = "Tarik dan lepas file dari Windows Explorer ke area ini.";
            return;
        }

        ShelfHintText.Text = $"{_state.ShelfFiles.Count} file terparkir · referensi cepat";
        foreach (var path in _state.ShelfFiles.ToList())
        {
            var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 0), LastChildFill = true };

            var removeBtn = new Button
            {
                Content = "×",
                Style = (Style)FindResource("PikoButton"),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) =>
            {
                _state.ShelfFiles.Remove(path);
                SaveAppState();
                UpdateShelfUI();
            };
            DockPanel.SetDock(removeBtn, Dock.Right);
            dock.Children.Add(removeBtn);

            var fileBtn = new TextBlock
            {
                Text = Path.GetFileName(path),
                ToolTip = $"{path}\n(Klik untuk membuka di Windows Explorer)",
                Foreground = (Brush)FindResource("MainTextBrush"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = Cursors.Hand
            };
            fileBtn.MouseLeftButtonUp += (_, _) =>
            {
                try
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    }
                }
                catch { }
            };
            dock.Children.Add(fileBtn);

            ShelfItemsPanel.Children.Add(dock);
        }
    }

    private void ClearShelf_Click(object sender, RoutedEventArgs e)
    {
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
