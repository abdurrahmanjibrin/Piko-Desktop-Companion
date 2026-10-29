using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Piko.Models;

namespace Piko.Services;

/// <summary>
/// Centralized controller for Piko's minimal line eyes and headset visuals.
/// All eye expressions, blinking, tracking, and headphone mode transitions are coordinated here.
/// </summary>
public sealed class PikoEyeController
{
    private readonly Grid _eyePair;
    private readonly Path _leftEyeLine;
    private readonly Path _rightEyeLine;
    private readonly ScaleTransform _leftEyeScale;
    private readonly ScaleTransform _rightEyeScale;
    private readonly RotateTransform _leftEyeRotate;
    private readonly RotateTransform _rightEyeRotate;
    private readonly TranslateTransform _eyePairTranslate;
    private readonly System.Windows.Media.Effects.DropShadowEffect _eyeGlowEffect;

    private readonly Grid? _headsetVisual;
    private readonly Path? _headsetArc;
    private readonly Border? _leftEarcup;
    private readonly Border? _rightEarcup;
    private readonly ScaleTransform? _headsetScale;
    private readonly TranslateTransform? _headsetTranslate;

    private static readonly SolidColorBrush ActiveEyeBrush = new(Color.FromRgb(213, 138, 255)); // #D58AFF
    private static readonly SolidColorBrush SleepyEyeBrush = new(Color.FromRgb(150, 131, 185)); // #9683B9
    private static readonly Color GlowPurple = Color.FromRgb(189, 135, 255); // #BD87FF

    // Geometries for eye shapes:
    // Idle & Open: Vertical rounded pill bars (Line from Y=2.5 to Y=12.5 with StrokeThickness 4.5 and Round caps)
    public static readonly Geometry IdleEyeGeom = Geometry.Parse("M 7 2.5 L 7 12.5");
    public static readonly Geometry OpenEyeGeom = Geometry.Parse("M 7 2.5 L 7 12.5");
    public static readonly Geometry SuccessEyeGeom = Geometry.Parse("M 1 7.5 Q 7 1.5 13 7.5"); // Happy upward arc
    public static readonly Geometry SlitEyeGeom = Geometry.Parse("M 1 7 L 13 7"); // Focus slit
    public static readonly Geometry ThinkLeftGeom = Geometry.Parse("M 1 8 L 13 6");
    public static readonly Geometry ThinkRightGeom = Geometry.Parse("M 1 6 L 13 8");
    public static readonly Geometry SleepyLeftGeom = Geometry.Parse("M 1 3.5 Q 7 7.5 13 3.5"); // Downward drooping closed curve
    public static readonly Geometry SleepyRightGeom = Geometry.Parse("M 1 3.5 Q 7 7.5 13 3.5");

    private bool _isHeadsetVisible;
    private bool _isBlinking;
    private PikoEmotion _currentEmotion = PikoEmotion.Idle;

    public PikoEmotion CurrentEmotion => _currentEmotion;
    public bool IsHeadsetVisible => _isHeadsetVisible;

    public PikoEyeController(
        Grid eyePair,
        Path leftEyeLine,
        Path rightEyeLine,
        ScaleTransform leftEyeScale,
        ScaleTransform rightEyeScale,
        RotateTransform leftEyeRotate,
        RotateTransform rightEyeRotate,
        TranslateTransform eyePairTranslate,
        System.Windows.Media.Effects.DropShadowEffect eyeGlowEffect,
        Grid? headsetVisual = null,
        Path? headsetArc = null,
        Border? leftEarcup = null,
        Border? rightEarcup = null,
        ScaleTransform? headsetScale = null,
        TranslateTransform? headsetTranslate = null)
    {
        _eyePair = eyePair;
        _leftEyeLine = leftEyeLine;
        _rightEyeLine = rightEyeLine;
        _leftEyeScale = leftEyeScale;
        _rightEyeScale = rightEyeScale;
        _leftEyeRotate = leftEyeRotate;
        _rightEyeRotate = rightEyeRotate;
        _eyePairTranslate = eyePairTranslate;
        _eyeGlowEffect = eyeGlowEffect;

        _headsetVisual = headsetVisual;
        _headsetArc = headsetArc;
        _leftEarcup = leftEarcup;
        _rightEarcup = rightEarcup;
        _headsetScale = headsetScale;
        _headsetTranslate = headsetTranslate;
    }

    /// <summary>
    /// Applies the specified emotion expression with smooth transitions.
    /// </summary>
    public void ApplyEmotion(PikoEmotion emotion, bool reducedMotion = false)
    {
        bool emotionChanged = _currentEmotion != emotion;
        _currentEmotion = emotion;

        if (_isBlinking)
        {
            _isBlinking = false;
            _leftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _rightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        _eyePair.Opacity = 1.0;
        _leftEyeRotate.Angle = 0;
        _rightEyeRotate.Angle = 0;
        _leftEyeScale.ScaleX = 1.0;
        _leftEyeScale.ScaleY = 1.0;
        _rightEyeScale.ScaleX = 1.0;
        _rightEyeScale.ScaleY = 1.0;

        switch (emotion)
        {
            case PikoEmotion.Idle:
                _leftEyeLine.StrokeThickness = 4.5;
                _rightEyeLine.StrokeThickness = 4.5;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = IdleEyeGeom;
                _rightEyeLine.Data = IdleEyeGeom;
                _eyePair.Opacity = 1.0;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 5;
                _eyeGlowEffect.Opacity = 0.45;
                break;

            case PikoEmotion.Hover:
                _leftEyeLine.StrokeThickness = 4.8;
                _rightEyeLine.StrokeThickness = 4.8;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = OpenEyeGeom;
                _rightEyeLine.Data = OpenEyeGeom;
                _leftEyeScale.ScaleX = 1.06;
                _leftEyeScale.ScaleY = 1.06;
                _rightEyeScale.ScaleX = 1.06;
                _rightEyeScale.ScaleY = 1.06;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 7;
                _eyeGlowEffect.Opacity = 0.60;
                break;

            case PikoEmotion.Thinking:
                _leftEyeLine.StrokeThickness = 2.4;
                _rightEyeLine.StrokeThickness = 2.4;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = ThinkLeftGeom;
                _rightEyeLine.Data = ThinkRightGeom;
                _leftEyeScale.ScaleY = 0.85;
                _rightEyeScale.ScaleY = 0.85;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 4;
                _eyeGlowEffect.Opacity = 0.35;
                break;

            case PikoEmotion.Listening:
                // Music playback: vertical eyes attentive, wide open wearing headset
                _leftEyeLine.StrokeThickness = 4.5;
                _rightEyeLine.StrokeThickness = 4.5;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = OpenEyeGeom;
                _rightEyeLine.Data = OpenEyeGeom;
                _eyePair.Opacity = 1.0;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 5;
                _eyeGlowEffect.Opacity = 0.45;
                break;

            case PikoEmotion.Working:
                _leftEyeLine.StrokeThickness = 2.4;
                _rightEyeLine.StrokeThickness = 2.4;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = SlitEyeGeom;
                _rightEyeLine.Data = SlitEyeGeom;
                _leftEyeScale.ScaleY = 0.85;
                _rightEyeScale.ScaleY = 0.85;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 4;
                _eyeGlowEffect.Opacity = 0.40;
                break;

            case PikoEmotion.Success:
                _leftEyeLine.StrokeThickness = 2.2;
                _rightEyeLine.StrokeThickness = 2.2;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = SuccessEyeGeom;
                _rightEyeLine.Data = SuccessEyeGeom;
                _leftEyeScale.ScaleX = 1.08;
                _leftEyeScale.ScaleY = 1.08;
                _rightEyeScale.ScaleX = 1.08;
                _rightEyeScale.ScaleY = 1.08;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 6;
                _eyeGlowEffect.Opacity = 0.55;
                TriggerBounce();
                break;

            case PikoEmotion.Error:
                _leftEyeLine.StrokeThickness = 2.4;
                _rightEyeLine.StrokeThickness = 2.4;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = SlitEyeGeom;
                _rightEyeLine.Data = SlitEyeGeom;
                _leftEyeRotate.Angle = -6;
                _rightEyeRotate.Angle = 6;
                _leftEyeScale.ScaleY = 0.85;
                _rightEyeScale.ScaleY = 0.85;
                _eyeGlowEffect.Color = Color.FromRgb(244, 114, 138);
                _eyeGlowEffect.BlurRadius = 5;
                _eyeGlowEffect.Opacity = 0.50;
                break;

            case PikoEmotion.Sleepy:
                _leftEyeLine.StrokeThickness = 2.0;
                _rightEyeLine.StrokeThickness = 2.0;
                _leftEyeLine.Stroke = SleepyEyeBrush;
                _rightEyeLine.Stroke = SleepyEyeBrush;
                _leftEyeLine.Data = SleepyLeftGeom;
                _rightEyeLine.Data = SleepyRightGeom;
                _eyePair.Opacity = 0.70;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.Opacity = 0.20;
                _eyeGlowEffect.BlurRadius = 3;
                break;

            case PikoEmotion.Alert:
                _leftEyeLine.StrokeThickness = 4.8;
                _rightEyeLine.StrokeThickness = 4.8;
                _leftEyeLine.Stroke = ActiveEyeBrush;
                _rightEyeLine.Stroke = ActiveEyeBrush;
                _leftEyeLine.Data = OpenEyeGeom;
                _rightEyeLine.Data = OpenEyeGeom;
                _leftEyeScale.ScaleX = 1.10;
                _leftEyeScale.ScaleY = 1.12;
                _rightEyeScale.ScaleX = 1.10;
                _rightEyeScale.ScaleY = 1.12;
                _eyeGlowEffect.Color = GlowPurple;
                _eyeGlowEffect.BlurRadius = 7;
                _eyeGlowEffect.Opacity = 0.65;
                TriggerBounce();
                break;
        }

        if (!reducedMotion && emotionChanged && emotion is not PikoEmotion.Sleepy)
        {
            var popAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            _leftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
            _rightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
        }
    }

    /// <summary>
    /// Performs a natural eye blink (closes briefly and re-opens smoothly).
    /// </summary>
    public async Task BlinkAsync()
    {
        if (_currentEmotion is PikoEmotion.Sleepy or PikoEmotion.Success)
            return;

        _isBlinking = true;
        var originalScaleY = _leftEyeScale.ScaleY;

        var closeAnim = new DoubleAnimation
        {
            To = 0.08,
            Duration = TimeSpan.FromMilliseconds(70),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        _leftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnim);
        _rightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnim);

        await Task.Delay(85);

        if (!_isBlinking) return;

        var openAnim = new DoubleAnimation
        {
            To = originalScaleY,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        openAnim.Completed += (_, _) =>
        {
            _leftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _rightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _leftEyeScale.ScaleY = originalScaleY;
            _rightEyeScale.ScaleY = originalScaleY;
            _isBlinking = false;
        };

        _leftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, openAnim);
        _rightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, openAnim);
    }

    /// <summary>
    /// Toggles or sets the visibility of the minimalist headphone accessory.
    /// </summary>
    public void SetHeadsetVisible(bool visible, bool isPlaying = true, bool animated = true)
    {
        _isHeadsetVisible = visible;
        if (_headsetVisual == null) return;

        if (!visible)
        {
            if (!animated)
            {
                _headsetVisual.BeginAnimation(UIElement.OpacityProperty, null);
                _headsetVisual.Visibility = Visibility.Collapsed;
                _headsetVisual.Opacity = 0.0;
                if (_headsetTranslate != null)
                {
                    _headsetTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    _headsetTranslate.Y = -6;
                }
                return;
            }

            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (_, _) =>
            {
                if (!_isHeadsetVisible)
                {
                    _headsetVisual.Visibility = Visibility.Collapsed;
                    _headsetVisual.Opacity = 0.0;
                    if (_headsetTranslate != null) _headsetTranslate.Y = -6;
                }
            };
            _headsetVisual.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            if (_headsetTranslate != null)
            {
                var liftAnim = new DoubleAnimation(-6, -11, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                _headsetTranslate.BeginAnimation(TranslateTransform.YProperty, liftAnim);
            }
            return;
        }

        // Cancel any pending fadeOut/lift animations
        _headsetVisual.BeginAnimation(UIElement.OpacityProperty, null);
        if (_headsetTranslate != null)
        {
            _headsetTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            _headsetTranslate.Y = -6;
        }
        if (_headsetScale != null)
        {
            _headsetScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _headsetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _headsetScale.ScaleX = 1.0;
            _headsetScale.ScaleY = 1.0;
        }

        _headsetVisual.Visibility = Visibility.Visible;
        if (!animated)
        {
            _headsetVisual.Opacity = 1.0;
            return;
        }

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _headsetVisual.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        if (_headsetScale != null)
        {
            var scaleAnim = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            _headsetScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            _headsetScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }

        if (_headsetTranslate != null)
        {
            var landAnim = new DoubleAnimation(-11, -6, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            };
            _headsetTranslate.BeginAnimation(TranslateTransform.YProperty, landAnim);
        }
    }

    /// <summary>
    /// Subtly tracks the mouse cursor within normalized bounds (-1 to 1).
    /// </summary>
    public void TrackCursor(double normX, double normY)
    {
        if (_currentEmotion == PikoEmotion.Success || _currentEmotion == PikoEmotion.Sleepy)
            return;

        _eyePairTranslate.X = normX * 3.2;
        _eyePairTranslate.Y = normY * 2.0;

        if (_currentEmotion == PikoEmotion.Hover)
        {
            _leftEyeRotate.Angle = normX * 2.5;
            _rightEyeRotate.Angle = normX * 2.5;
        }
    }

    /// <summary>
    /// Resets cursor tracking offsets back to center.
    /// </summary>
    public void ResetTracking()
    {
        _eyePairTranslate.X = 0;
        _eyePairTranslate.Y = 0;
        if (_currentEmotion != PikoEmotion.Sleepy && _currentEmotion != PikoEmotion.Thinking)
        {
            _leftEyeRotate.Angle = 0;
            _rightEyeRotate.Angle = 0;
        }
    }

    /// <summary>
    /// Triggers a subtle vertical bounce animation on the eye pair.
    /// </summary>
    public void TriggerBounce()
    {
        var anim = new DoubleAnimation
        {
            From = 0,
            To = -5,
            Duration = TimeSpan.FromMilliseconds(120),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _eyePairTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    /// <summary>
    /// Advances one step in the AI working focus animation.
    /// </summary>
    public void WorkingTick(int step)
    {
        if (_currentEmotion != PikoEmotion.Working) return;
        var offset = step switch
        {
            0 => -1.2,
            1 => 0.0,
            2 => 1.2,
            _ => 0.0
        };
        _eyePairTranslate.X = offset;
    }
}
