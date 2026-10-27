namespace Piko.Models;

public sealed class BridgeMessage
{
    public string? Type { get; set; }
    public string? Token { get; set; }
    public string? Source { get; set; }
    public string? Title { get; set; }
    public string? Channel { get; set; }
    public string? VideoId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? PageUrl { get; set; }
    public double? Duration { get; set; }
    public double? CurrentTime { get; set; }
    public string? PlaybackState { get; set; }
    public int? TabId { get; set; }
    public string? CommandId { get; set; }
    public string? CommandAction { get; set; }
    public bool? CommandSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public long? Timestamp { get; set; }

    public const string TypeStart = "ai-start";
    public const string TypeComplete = "ai-complete";
    public const string TypeCancel = "ai-cancel";
    public const string TypeGenerationStarted = "generation_started";
    public const string TypeGenerationCompleted = "generation_completed";
    public const string TypeGenerationCancelled = "generation_cancelled";
    public const string TypeDiagnosticsRecover = "diagnostics-recover";
    public const string TypeToggleVisual = "toggle-visual";
    public const string TypeMediaPlaying = "media_playing";
    public const string TypeMediaPaused = "media_paused";
    public const string TypeMediaStopped = "media_stopped";
    public const string TypeMediaPlayingAlt = "media-playing";
    public const string TypeMediaPausedAlt = "media-paused";
    public const string TypeMediaStoppedAlt = "media-stopped";
    public const string TypeMediaMetadata = "media_metadata_changed";
    public const string TypeMediaMetadataAlt = "media_metadata";
    public const string TypeMediaProgress = "media_progress_updated";
    public const string TypeMediaCommandResult = "media_command_result";

    public bool IsValid(string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(Token) || Token != expectedToken)
            return false;

        return Type is TypeStart or TypeComplete or TypeCancel
            or TypeGenerationStarted or TypeGenerationCompleted or TypeGenerationCancelled
            or TypeDiagnosticsRecover or TypeToggleVisual
            or TypeMediaPlaying or TypeMediaPaused or TypeMediaStopped
            or TypeMediaPlayingAlt or TypeMediaPausedAlt or TypeMediaStoppedAlt
            or TypeMediaMetadata or TypeMediaMetadataAlt or TypeMediaProgress
            or TypeMediaCommandResult;
    }
}
