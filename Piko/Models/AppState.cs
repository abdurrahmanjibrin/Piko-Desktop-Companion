namespace Piko.Models;

public sealed class AppState
{
    // Water tracking
    public int WaterCount { get; set; }
    public DateTime WaterDate { get; set; } = DateTime.Today;
    public bool WaterReminderEnabled { get; set; } = true;
    public int WaterReminderIntervalMinutes { get; set; } = 45;
    public DateTime LastDrinkTime { get; set; } = DateTime.Now;

    // Little tasks
    public List<TodoEntry> Todos { get; set; } = new();

    // Shelf items
    public List<string> ShelfFiles { get; set; } = new();

    // Settings
    public bool SoundEnabled { get; set; } = true;
    public bool DoNotDisturb { get; set; } = false;
    public bool ReducedMotion { get; set; } = false;
    public bool DarkMode { get; set; } = false;
    public int AutoCollapseDelayMs { get; set; } = 500;
    public string BridgeToken { get; set; } = "piko-dev-bridge-token-2026";
    public int BridgePort { get; set; } = 17321;

    // Screen edge docking
    public string DockEdge { get; set; } = "Top"; // "Top", "Left", "Right"
    public double CustomLeft { get; set; } = 0;
    public double CustomTop { get; set; } = 0;
    public bool IsCustomPosition { get; set; } = false;

    // Recent AI events log
    public List<RecentAiEvent> RecentAiEvents { get; set; } = new();

    // Operation journal history for undo
    public List<OperationJournal> OperationJournals { get; set; } = new();

    // Custom storage organization target folders (e.g. "Tugas", "Kuliah", "Dokumen", etc.)
    public List<string> CustomTargetFolders { get; set; } = new() { "Tugas", "Kuliah", "Dokumen" };

    // Google Calendar multi-account sync & offline events cache
    public List<GoogleAccountEntry> GoogleAccounts { get; set; } = new();
    public List<CalendarEventItem> CalendarEvents { get; set; } = new();
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;

    // Pomodoro auto-interval settings
    public int PomodoroWorkDurationMinutes { get; set; } = 25;
    public int PomodoroShortBreakMinutes { get; set; } = 5;
    public int PomodoroLongBreakMinutes { get; set; } = 15;
    public bool PomodoroAutoStartNext { get; set; } = true;
    public string PomodoroActiveTask { get; set; } = string.Empty;
}

public sealed class GoogleAccountEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AccountColor { get; set; } = "#3B82F6"; // Hex color for event tag / badge
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime TokenExpiry { get; set; } = DateTime.MinValue;
    public bool IsActive { get; set; } = true;
    public DateTime LastSyncTime { get; set; } = DateTime.MinValue;
    public string IcalUrl { get; set; } = string.Empty;
    public string SyncMethod { get; set; } = "OAuth"; // "OAuth" or "iCal"
    public string AccountColorHex { get => AccountColor; set => AccountColor = value; }
}

public sealed class CalendarEventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AccountEmail { get; set; } = string.Empty;
    public string AccountColor { get; set; } = "#3B82F6";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string Location { get; set; } = string.Empty;
    public string MeetUrl { get; set; } = string.Empty;
    public string HtmlLink { get; set; } = string.Empty;
    public string ColorHex { get => AccountColor; set => AccountColor = value; }
    public string MeetLink { get => MeetUrl; set => MeetUrl = value; }
}

public sealed class RecentAiEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = "ChatGPT";
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}

public sealed class TodoEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
