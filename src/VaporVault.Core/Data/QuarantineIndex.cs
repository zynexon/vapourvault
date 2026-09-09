using System.Text.Json;
using VaporVault.Core.Models;

namespace VaporVault.Core.Data;

/// <summary>
/// Manages the flat JSON quarantine index at C:\ProgramData\VaporVault\quarantine-index.json.
/// This is the central registry of all quarantined apps, used by the Vault page.
/// Kept as flat JSON for inspectability and simplicity (v1 requirement).
/// </summary>
public class QuarantineIndex
{
    private readonly string _indexPath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Default quarantine base path.
    /// </summary>
    public static string DefaultQuarantineBasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VaporVault", "Quarantine");

    /// <summary>
    /// Default index file path.
    /// </summary>
    public static string DefaultIndexPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VaporVault", "quarantine-index.json");

    public QuarantineIndex() : this(DefaultIndexPath) { }

    public QuarantineIndex(string indexPath)
    {
        _indexPath = indexPath;
    }

    /// <summary>
    /// Gets all quarantine entries.
    /// </summary>
    public IReadOnlyList<QuarantineEntry> GetAll()
    {
        lock (_lock)
        {
            return ReadIndex();
        }
    }

    /// <summary>
    /// Gets active (non-expired, non-deleted) quarantine entries.
    /// </summary>
    public IReadOnlyList<QuarantineEntry> GetActive()
    {
        return GetAll()
            .Where(e => e.Status == QuarantineStatus.Active)
            .ToList();
    }

    /// <summary>
    /// Adds a new quarantine entry to the index.
    /// </summary>
    public void Add(QuarantineEntry entry)
    {
        lock (_lock)
        {
            var entries = ReadIndex();
            entries.Add(entry);
            WriteIndex(entries);
        }
    }

    /// <summary>
    /// Updates an existing entry by quarantine ID.
    /// </summary>
    public bool Update(string quarantineId, Action<QuarantineEntry> updateAction)
    {
        lock (_lock)
        {
            var entries = ReadIndex();
            var entry = entries.FirstOrDefault(e => e.QuarantineId == quarantineId);
            if (entry == null) return false;

            updateAction(entry);
            WriteIndex(entries);
            return true;
        }
    }

    /// <summary>
    /// Removes an entry by quarantine ID.
    /// </summary>
    public bool Remove(string quarantineId)
    {
        lock (_lock)
        {
            var entries = ReadIndex();
            var removed = entries.RemoveAll(e => e.QuarantineId == quarantineId);
            if (removed > 0)
            {
                WriteIndex(entries);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets a single entry by quarantine ID.
    /// </summary>
    public QuarantineEntry? GetById(string quarantineId)
    {
        return GetAll().FirstOrDefault(e => e.QuarantineId == quarantineId);
    }

    private List<QuarantineEntry> ReadIndex()
    {
        if (!File.Exists(_indexPath))
            return [];

        try
        {
            var json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<List<QuarantineEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void WriteIndex(List<QuarantineEntry> entries)
    {
        var dir = Path.GetDirectoryName(_indexPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(_indexPath, json);
    }
}
