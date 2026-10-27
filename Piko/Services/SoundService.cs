using System.Media;

namespace Piko.Services;

public sealed class SoundService
{
    public bool SoundEnabled { get; set; } = true;
    public bool DoNotDisturb { get; set; } = false;

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

    public void PlayGentleTap()
    {
        if (!SoundEnabled || DoNotDisturb) return;

        try
        {
            SystemSounds.Beep.Play();
        }
        catch
        {
            // Fallback gracefully
        }
    }
}
