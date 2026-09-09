using ZstdSharp;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Background compression service using zstd.
/// Runs after the quarantine move completes — the instant move must never block on compression.
/// Compresses all files in the quarantine folder and updates the manifest.
/// </summary>
public class CompressionService
{
    private const int CompressionLevel = 3; // zstd default — good speed/ratio trade-off
    private const string CompressedExtension = ".zst";

    /// <summary>
    /// Result of a compression operation.
    /// </summary>
    public record CompressionResult(
        bool Success,
        long OriginalSizeBytes,
        long CompressedSizeBytes,
        int FilesCompressed,
        string? ErrorMessage);

    /// <summary>
    /// Compresses all files in the quarantine folder in-place using zstd.
    /// Each file is compressed to a .zst companion and the original is deleted.
    /// Updates the manifest when complete.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compression result with size savings.</returns>
    public async Task<CompressionResult> CompressAsync(
        string quarantineFolderPath,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Compress(quarantineFolderPath, progress, cancellationToken), cancellationToken);
    }

    private CompressionResult Compress(
        string quarantineFolderPath,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // Don't compress the manifest, restore.bat, or registry export
            var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "manifest.json", "restore.bat", "registry_backup.reg"
            };

            var files = Directory.GetFiles(quarantineFolderPath, "*", SearchOption.AllDirectories)
                .Where(f => !excludedFiles.Contains(Path.GetFileName(f)))
                .Where(f => !f.EndsWith(CompressedExtension, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length == 0)
                return new CompressionResult(true, 0, 0, 0, null);

            long originalSize = 0;
            long compressedSize = 0;
            int filesCompressed = 0;

            for (int i = 0; i < files.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[i];
                var compressedPath = file + CompressedExtension;

                try
                {
                    var fileInfo = new FileInfo(file);
                    originalSize += fileInfo.Length;

                    using (var input = File.OpenRead(file))
                    using (var output = File.Create(compressedPath))
                    using (var compressor = new CompressionStream(output, CompressionLevel))
                    {
                        input.CopyTo(compressor);
                    }

                    var compressedInfo = new FileInfo(compressedPath);
                    compressedSize += compressedInfo.Length;

                    // Delete original after successful compression
                    File.Delete(file);
                    filesCompressed++;
                }
                catch (IOException)
                {
                    // File may be locked — skip it, don't fail the whole operation
                    if (File.Exists(compressedPath))
                    {
                        try { File.Delete(compressedPath); } catch { }
                    }
                }

                progress?.Invoke((double)(i + 1) / files.Length);
            }

            // Update manifest
            var manifestWriter = new ManifestWriter();
            manifestWriter.UpdateManifest(quarantineFolderPath, m =>
            {
                m.Compressed = true;
                m.CompressedSizeBytes = compressedSize;
            });

            return new CompressionResult(true, originalSize, compressedSize, filesCompressed, null);
        }
        catch (OperationCanceledException)
        {
            return new CompressionResult(false, 0, 0, 0, "Compression was cancelled.");
        }
        catch (Exception ex)
        {
            return new CompressionResult(false, 0, 0, 0, ex.Message);
        }
    }

    /// <summary>
    /// Decompresses all .zst files in a quarantine folder back to their original format.
    /// Used by the Revive flow before restoring files.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DecompressAsync(string quarantineFolderPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var compressedFiles = Directory.GetFiles(quarantineFolderPath, $"*{CompressedExtension}", SearchOption.AllDirectories);

            foreach (var compressedFile in compressedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var originalPath = compressedFile[..^CompressedExtension.Length];

                using (var input = File.OpenRead(compressedFile))
                using (var output = File.Create(originalPath))
                using (var decompressor = new DecompressionStream(input))
                {
                    decompressor.CopyTo(output);
                }

                File.Delete(compressedFile);
            }
        }, cancellationToken);
    }
}
