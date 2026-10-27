namespace Piko.Models;

public enum PikoEmotion
{
    Idle,
    Hover,
    Thinking,
    Listening,
    Working,
    Success,
    Error,
    Sleepy,
    Alert
}

public static class PikoEmotionExtensions
{
    public static string GetDefaultStatusText(this PikoEmotion emotion) => emotion switch
    {
        PikoEmotion.Idle => "piko is snoozing peacefully",
        PikoEmotion.Hover => "hello there! how can I help?",
        PikoEmotion.Thinking => "piko is thinking...",
        PikoEmotion.Listening => "listening carefully ✨",
        PikoEmotion.Working => "processing in the background...",
        PikoEmotion.Success => "yay! task completed! ✨",
        PikoEmotion.Error => "oops! something went wrong",
        PikoEmotion.Sleepy => "feeling a bit sleepy...",
        PikoEmotion.Alert => "look here! new notification!",
        _ => "your little desk companion"
    };

    public static string GetEmotionBadge(this PikoEmotion emotion) => emotion switch
    {
        PikoEmotion.Idle => "IDLE",
        PikoEmotion.Hover => "READY",
        PikoEmotion.Thinking => "THINKING",
        PikoEmotion.Listening => "LISTENING",
        PikoEmotion.Working => "WORKING",
        PikoEmotion.Success => "DONE ✨",
        PikoEmotion.Error => "ERROR",
        PikoEmotion.Sleepy => "SNOOZING",
        PikoEmotion.Alert => "ALERT",
        _ => "STANDBY"
    };
}
