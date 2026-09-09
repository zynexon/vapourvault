using System.Text.Json;
using VaporVault.Core.Models;

namespace VaporVault.Core.Lifecycle;

/// <summary>
/// Logs permanent deletions and other significant actions to a local history file.
/// The user can review this list in the History page to see what was permanently deleted and when.
/// </summary>
public class HistoryLogger
{
    private readonly string _historyPath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string DefaultHistoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VaporVault", "history.json");

    public HistoryLogger() : this(DefaultHistoryPath) { }

    public HistoryLogger(string historyPath)
    {
        _historyPath = historyPath;
    }

    /// <summary>
    /// Represents a single history entry.
    /// </summary>
    public record HistoryEntry(
        string AppName,
        string QuarantineId,
        DateTime QuarantinedAt,
        DateTime DeletedAt,
        long SizeBytes,
        string Action); // "auto-deleted", "manually-deleted", "revived"

    /// <summary>
    /// Logs the permanent deletion of a quarantine entry.
    /// </summary>
    public void LogDeletion(QuarantineEntry entry)
    {
        AddEntry(new HistoryEntry(
            entry.AppName,
            entry.QuarantineId,
            entry.QuarantinedAt,
            DateTime.UtcNow,
            entry.TotalSizeBytes,
            entry.DaysRemaining <= 0 ? "auto-deleted" : "manually-deleted"));
    }

    /// <summary>
    /// Logs a revive action.
    /// </summary>
    public void LogRevive(QuarantineEntry entry)
    {
        AddEntry(new HistoryEntry(
            entry.AppName,
            entry.QuarantineId,
            entry.QuarantinedAt,
            DateTime.UtcNow,
            entry.TotalSizeBytes,
            "revived"));
    }

    /// <summary>
    /// Gets all history entries, most recent first.
    /// </summary>
    public IReadOnlyList<HistoryEntry> GetHistory()
    {
        lock (_lock)
        {
            return ReadHistory()
                .OrderByDescending(e => e.DeletedAt)
                .ToList();
        }
    }

    private void AddEntry(HistoryEntry entry)
    {
        lock (_lock)
        {
            var history = ReadHistory();
            history.Add(entry);

            // Keep last 100 entries to avoid unbounded growth
            if (history.Count > 100)
                history.RemoveRange(0, history.Count - 100);

            WriteHistory(history);
        }
    }

    private List<HistoryEntry> ReadHistory()
    {
        if (!File.Exists(_historyPath))
            return [];

        try
        {
            var json = File.ReadAllText(_historyPath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void WriteHistory(List<HistoryEntry> entries)
    {
        var dir = Path.GetDirectoryName(_historyPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(_historyPath, json);
    }
}
