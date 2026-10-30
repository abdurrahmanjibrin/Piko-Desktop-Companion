using System.Media;

namespace Piko.Services;

public sealed class SoundService
{
    public bool SoundEnabled { get; set; } = true;
    public bool DoNotDisturb { get; set; } = false;

    /// <summary>
    /// Plays celebratory / success chime for Piko's reactions (AI complete, Pomodoro done, water drank, etc.)
    /// </summary>
    public void PlaySuccessChime()
    {
        if (!SoundEnabled || DoNotDisturb) return;

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Fallback gracefully
        }
    }

    /// <summary>
    /// Plays attention / alert chime for Piko's reminders (Water reminder alert, break alert, error emotion)
    /// </summary>
    public void PlayAlertSound()
    {
        if (!SoundEnabled || DoNotDisturb) return;

        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // Fallback gracefully
        }
    }

    /// <summary>
    /// UI click and tap sounds are permanently disabled.
    /// Audio feedback is strictly reserved for Piko's reactions, emotions, and alerts.
    /// </summary>
    public void PlayGentleTap()
    {
        // Intentionally silent: No sound on clicks
    }
}
