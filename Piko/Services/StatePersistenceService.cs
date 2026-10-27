using System.IO;
using System.Text.Json;
using Piko.Models;

namespace Piko.Services;

public sealed class StatePersistenceService
{
    private readonly string _filePath;

    public StatePersistenceService(string? customPath = null)
    {
        _filePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Piko",
            "state.json");
    }

    public AppState Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var state = JsonSerializer.Deserialize<AppState>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (state is not null)
                {
                    state.Todos ??= new List<TodoEntry>();
                    state.ShelfFiles ??= new List<string>();
                    state.OperationJournals ??= new List<OperationJournal>();
                    state.RecentAiEvents ??= new List<RecentAiEvent>();

                    if (state.WaterDate.Date != DateTime.Today)
                    {
                        state.WaterDate = DateTime.Today;
                        state.WaterCount = 0;
                        Save(state);
                    }

                    return state;
                }
            }
        }
        catch
        {
            // Fall back cleanly to fresh default state if corrupted
        }

        var fresh = new AppState();
        Save(fresh);
        return fresh;
    }

    public void Save(AppState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Persistence failure does not crash the desktop companion
        }
    }
}
