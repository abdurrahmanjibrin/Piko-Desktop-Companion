using System;
using System.Diagnostics;
using System.IO;
using System.Timers;

namespace Piko.Services;

/// <summary>
/// Monitors Google Antigravity IDE activity by watching local session logs and databases.
/// Emits real-time start/complete events to Piko's AI Bridge when Antigravity generates responses.
/// </summary>
public sealed class AntigravityWatcherService : IDisposable
{
    private FileSystemWatcher? _conversationsWatcher;
    private FileSystemWatcher? _brainWatcher;
    private readonly System.Timers.Timer _quietTimer;
    private bool _isThinking;
    private readonly object _lock = new();

    public event Action<string, string>? AntigravityStarted;
    public event Action<string, string>? AntigravityCompleted;
    public event Action<string>? DiagnosticLogged;

    public bool IsWatching { get; private set; }

    public AntigravityWatcherService()
    {
        // 3.5 seconds quiet period after the last write indicates generation completion
        _quietTimer = new System.Timers.Timer(3500) { AutoReset = false };
        _quietTimer.Elapsed += QuietTimer_Elapsed;
    }

    public void Start()
    {
        if (IsWatching) return;

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var geminiIdePath = Path.Combine(userProfile, ".gemini", "antigravity-ide");
            var conversationsPath = Path.Combine(geminiIdePath, "conversations");
            var brainPath = Path.Combine(geminiIdePath, "brain");

            if (Directory.Exists(conversationsPath))
            {
                _conversationsWatcher = new FileSystemWatcher(conversationsPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };
                _conversationsWatcher.Changed += OnActivityDetected;
                _conversationsWatcher.Created += OnActivityDetected;
            }

            if (Directory.Exists(brainPath))
            {
                _brainWatcher = new FileSystemWatcher(brainPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };
                _brainWatcher.Changed += OnActivityDetected;
                _brainWatcher.Created += OnActivityDetected;
            }

            IsWatching = true;
            DiagnosticLogged?.Invoke("AntigravityWatcherService started watching Antigravity directories.");
        }
        catch (Exception ex)
        {
            DiagnosticLogged?.Invoke($"AntigravityWatcherService.Start warning: {ex.Message}");
        }
    }

    private void OnActivityDetected(object sender, FileSystemEventArgs e)
    {
        // Only react to active conversation and message files
        var name = e.Name ?? string.Empty;
        bool isRelevant = name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith("transcript.jsonl", StringComparison.OrdinalIgnoreCase);
        if (!isRelevant)
        {
            return;
        }

        lock (_lock)
        {
            // Reset quiet timer on each write
            _quietTimer.Stop();
            _quietTimer.Start();

            if (!_isThinking)
            {
                _isThinking = true;
                DiagnosticLogged?.Invoke($"Antigravity active generation detected ({e.Name})");
                AntigravityStarted?.Invoke("Antigravity", "Antigravity sedang berpikir & bekerja...");
            }
        }
    }

    private void QuietTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (_isThinking)
            {
                _isThinking = false;
                DiagnosticLogged?.Invoke("Antigravity generation completed (quiet threshold reached).");
                AntigravityCompleted?.Invoke("Antigravity", "Antigravity selesai! ✨");
            }
        }
    }

    public void Stop()
    {
        if (!IsWatching) return;

        try
        {
            _quietTimer.Stop();
            if (_conversationsWatcher != null)
            {
                _conversationsWatcher.EnableRaisingEvents = false;
                _conversationsWatcher.Dispose();
                _conversationsWatcher = null;
            }

            if (_brainWatcher != null)
            {
                _brainWatcher.EnableRaisingEvents = false;
                _brainWatcher.Dispose();
                _brainWatcher = null;
            }

            IsWatching = false;
            DiagnosticLogged?.Invoke("AntigravityWatcherService stopped.");
        }
        catch (Exception ex)
        {
            DiagnosticLogged?.Invoke($"AntigravityWatcherService.Stop error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _quietTimer.Dispose();
    }
}
