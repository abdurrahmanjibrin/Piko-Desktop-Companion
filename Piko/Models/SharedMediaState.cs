namespace Piko.Models;

public sealed class SharedMediaState
{
    public string Source { get; set; } = "YouTube";
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string PlaybackState { get; set; } = "stopped"; // "playing" | "paused" | "stopped"
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public int? TabId { get; set; }
    public bool IsActive => PlaybackState == "playing";
    public bool HasMedia => !string.IsNullOrWhiteSpace(Title) && PlaybackState is "playing" or "paused";

    public void Reset()
    {
        Source = "YouTube";
        Title = string.Empty;
        Channel = string.Empty;
        PageUrl = string.Empty;
        VideoId = string.Empty;
        ThumbnailUrl = string.Empty;
        PlaybackState = "stopped";
        CurrentTime = 0;
        Duration = 0;
        TabId = null;
    }

    public string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return "0:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
