using VaporVault.Core.Data;
using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;

namespace VaporVault.Core.Lifecycle;

/// <summary>
/// Handles the Revive flow: restoring quarantined files back to their original locations.
/// Steps:
/// 1. Read manifest.json from quarantine folder
/// 2. Decompress if needed
/// 3. Move files back to original paths
/// 4. v3: Re-enable any scheduled tasks/services that were disabled
/// 5. Optionally offer to reapply .reg file (never automatic)
/// 6. Update quarantine index
/// </summary>
public class ReviveService
{
    private readonly CompressionService _compressionService;
    private readonly FileMover _fileMover;
    private readonly ManifestWriter _manifestWriter;
    private readonly QuarantineIndex _quarantineIndex;
    private readonly AppTraceDisabler _traceDisabler;

    public ReviveService() : this(new QuarantineIndex()) { }

    public ReviveService(QuarantineIndex quarantineIndex)
    {
        _compressionService = new CompressionService();
        _fileMover = new FileMover();
        _manifestWriter = new ManifestWriter();
        _quarantineIndex = quarantineIndex;
        _traceDisabler = new AppTraceDisabler();
    }

    /// <summary>
    /// Result of a revive operation.
    /// </summary>
    public record ReviveResult(
        bool Success,
        string AppName,
        int FilesRestored,
        int TracesReEnabled,
        bool RegistryFileAvailable,
        string? RegistryFilePath,
        string? ErrorMessage);

    /// <summary>
    /// Revives a quarantined app by restoring its files to original locations.
    /// </summary>
    /// <param name="quarantineId">The quarantine ID to revive.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReviveResult> ReviveAsync(
        string quarantineId,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entry = _quarantineIndex.GetById(quarantineId);
        if (entry == null)
            return new ReviveResult(false, "", 0, 0, false, null, $"Quarantine entry not found: {quarantineId}");

        var manifest = _manifestWriter.ReadManifest(entry.QuarantineFolderPath);
        if (manifest == null)
            return new ReviveResult(false, entry.AppName, 0, 0, false, null, "manifest.json not found or corrupted.");

        // Step 1: Decompress if needed
        if (manifest.Compressed)
        {
            progress?.Invoke(0.1);
            await _compressionService.DecompressAsync(entry.QuarantineFolderPath, cancellationToken);
            progress?.Invoke(0.3);
        }

        // Step 2: Move files back to original locations
        int totalFilesRestored = 0;
        int sourceIndex = 0;

        foreach (var source in manifest.Sources)
        {
            sourceIndex++;
            var quarantineSubPath = Path.Combine(entry.QuarantineFolderPath, source.QuarantineSubDir);

            if (!Directory.Exists(quarantineSubPath))
                continue;

            var moveResult = _fileMover.MoveDirectory(quarantineSubPath, source.OriginalPath, p =>
            {
                var baseProgress = manifest.Compressed ? 0.3 : 0.0;
                var moveRange = manifest.Compressed ? 0.6 : 0.9;
                var overallProgress = baseProgress + moveRange * ((sourceIndex - 1 + p) / manifest.Sources.Count);
                progress?.Invoke(overallProgress);
            });

            totalFilesRestored += moveResult.FilesMoved;
        }

        // Step 3: v3 — Re-enable any scheduled tasks/services that were disabled
        int tracesReEnabled = 0;
        if (manifest.AdditionalTraces != null)
        {
            try
            {
                tracesReEnabled = _traceDisabler.ReEnableTraces(manifest.AdditionalTraces);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReviveService: Trace re-enable failed: {ex.Message}");
            }
        }

        // Step 4: Update index
        _quarantineIndex.Update(quarantineId, e => e.Status = QuarantineStatus.Revived);

        // Step 5: Clean up quarantine folder (best effort)
        try
        {
            if (Directory.Exists(entry.QuarantineFolderPath))
                Directory.Delete(entry.QuarantineFolderPath, recursive: true);
        }
        catch { /* Best effort */ }

        progress?.Invoke(1.0);

        // Check if registry file is available for optional manual reapply
        var regFilePath = manifest.RegistryExport != null
            ? Path.Combine(entry.QuarantineFolderPath, manifest.RegistryExport.FileName)
            : null;

        var regFileExists = regFilePath != null && File.Exists(regFilePath);

        return new ReviveResult(
            Success: true,
            AppName: manifest.AppName,
            FilesRestored: totalFilesRestored,
            TracesReEnabled: tracesReEnabled,
            RegistryFileAvailable: regFileExists,
            RegistryFilePath: regFileExists ? regFilePath : null,
            ErrorMessage: null);
    }
}

