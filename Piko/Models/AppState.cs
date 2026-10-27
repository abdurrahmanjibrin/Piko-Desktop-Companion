namespace Piko.Models;

public sealed class AppState
{
    // Water tracking
    public int WaterCount { get; set; }
    public DateTime WaterDate { get; set; } = DateTime.Today;

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
