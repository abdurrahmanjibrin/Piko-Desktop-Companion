using System.Windows.Threading;
using Piko.Models;

namespace Piko.Services;

public sealed class PikoStateManager
{
    private readonly DispatcherTimer _autoRevertTimer;
    private PikoEmotion _currentEmotion = PikoEmotion.Idle;
    private string _statusMessage = "piko is snoozing peacefully";
    private string? _activeNotificationTitle;
    private string? _activeNotificationBody;
    private string? _activeNotificationSource;
    private bool _hasActiveNotification;

    public event Action<PikoEmotion, PikoEmotion>? EmotionChanged;
    public event Action<string>? StatusChanged;
    public event Action? NotificationChanged;

    public PikoEmotion CurrentEmotion
    {
        get => _currentEmotion;
        private set
        {
            if (_currentEmotion != value)
            {
                var old = _currentEmotion;
                _currentEmotion = value;
                EmotionChanged?.Invoke(old, value);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                StatusChanged?.Invoke(value);
            }
        }
    }

    public string? ActiveNotificationTitle => _activeNotificationTitle;
    public string? ActiveNotificationBody => _activeNotificationBody;
    public string? ActiveNotificationSource => _activeNotificationSource;
    public bool HasActiveNotification => _hasActiveNotification;

    public PikoStateManager()
    {
        _autoRevertTimer = new DispatcherTimer();
        _autoRevertTimer.Tick += (_, _) =>
        {
            _autoRevertTimer.Stop();
            if (CurrentEmotion is PikoEmotion.Success or PikoEmotion.Alert or PikoEmotion.Working or PikoEmotion.Thinking)
            {
                SetEmotion(PikoEmotion.Idle);
            }
        };
    }

    public void SetEmotion(PikoEmotion emotion, TimeSpan? autoRevert = null, string? customStatus = null)
    {
        _autoRevertTimer.Stop();
        CurrentEmotion = emotion;
        StatusMessage = customStatus ?? emotion.GetDefaultStatusText();

        if (autoRevert.HasValue && autoRevert.Value > TimeSpan.Zero)
        {
            _autoRevertTimer.Interval = autoRevert.Value;
            _autoRevertTimer.Start();
        }
    }

    public void TriggerNotification(string title, string body, string source = "ChatGPT")
    {
        _activeNotificationTitle = title;
        _activeNotificationBody = body;
        _activeNotificationSource = source;
        _hasActiveNotification = true;

        SetEmotion(PikoEmotion.Success, TimeSpan.FromSeconds(10), $"{source} is done! ✨");
        NotificationChanged?.Invoke();
    }

    public void DismissNotification()
    {
        _hasActiveNotification = false;
        _activeNotificationTitle = null;
        _activeNotificationBody = null;
        _activeNotificationSource = null;
        SetEmotion(PikoEmotion.Idle);
        NotificationChanged?.Invoke();
    }
}
