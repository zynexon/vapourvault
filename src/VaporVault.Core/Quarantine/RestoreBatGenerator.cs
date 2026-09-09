using System.Text;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Generates a self-contained restore.bat file inside the quarantine folder.
/// This batch file uses robocopy /MOVE to restore files to their original locations,
/// so the user can restore manually even without VaporVault installed.
/// </summary>
public class RestoreBatGenerator
{
    /// <summary>
    /// Generates a restore.bat file in the quarantine folder.
    /// </summary>
    /// <param name="quarantineFolderPath">Path to the quarantine folder.</param>
    /// <param name="manifest">The quarantine manifest with source information.</param>
    public void GenerateRestoreBat(string quarantineFolderPath, QuarantineManifest manifest)
    {
        var content = BuildBatContent(quarantineFolderPath, manifest);
        var batPath = Path.Combine(quarantineFolderPath, "restore.bat");
        File.WriteAllText(batPath, content, Encoding.ASCII);
    }

    /// <summary>
    /// Generates the content of a restore.bat file without writing it to disk.
    /// Useful for testing.
    /// </summary>
    public string GenerateRestoreBatContent(string quarantineFolderPath, QuarantineManifest manifest)
    {
        return BuildBatContent(quarantineFolderPath, manifest);
    }

    /// <summary>
    /// Shared implementation — builds the full restore.bat content string.
    /// </summary>
    private static string BuildBatContent(string quarantineFolderPath, QuarantineManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("REM ============================================================");
        sb.AppendLine($"REM VaporVault Restore Script for: {manifest.AppName}");
        sb.AppendLine($"REM Quarantined on: {manifest.QuarantinedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"REM Quarantine ID: {manifest.QuarantineId}");
        sb.AppendLine("REM");
        sb.AppendLine("REM This script restores quarantined files to their original");
        sb.AppendLine("REM locations. It uses robocopy /MOVE for each source folder.");
        sb.AppendLine("REM You can run this script even without VaporVault installed.");
        sb.AppendLine("REM ============================================================");
        sb.AppendLine();
        sb.AppendLine("echo.");
        sb.AppendLine($"echo Restoring files for: {manifest.AppName}");
        sb.AppendLine("echo.");
        sb.AppendLine();
        sb.AppendLine("set ERRORS=0");
        sb.AppendLine();

        foreach (var source in manifest.Sources)
        {
            var quarantineSubPath = Path.Combine(quarantineFolderPath, source.QuarantineSubDir);

            sb.AppendLine($"REM --- Restoring: {source.Type} ---");
            sb.AppendLine($"echo Restoring {source.Type} files to: {source.OriginalPath}");
            sb.AppendLine();

            // robocopy /MOVE /E copies the directory tree and removes source files
            // /NP = no progress, /NJH = no job header, /NJS = no job summary (cleaner output)
            sb.AppendLine($"robocopy \"{quarantineSubPath}\" \"{source.OriginalPath}\" /MOVE /E /NP /NJH /NJS");

            // robocopy exit codes: 0-7 are success/info, 8+ are errors
            sb.AppendLine("if %ERRORLEVEL% GEQ 8 (");
            sb.AppendLine($"    echo ERROR: Failed to restore {source.Type} files.");
            sb.AppendLine("    set /A ERRORS+=1");
            sb.AppendLine(") else (");
            sb.AppendLine($"    echo Successfully restored {source.Type} files.");
            sb.AppendLine(")");
            sb.AppendLine();
        }

        // Registry note
        if (manifest.RegistryExport != null)
        {
            sb.AppendLine("REM --- Registry Key ---");
            sb.AppendLine($"echo.");
            sb.AppendLine($"echo A registry export is available at:");
            sb.AppendLine($"echo   {Path.Combine(quarantineFolderPath, manifest.RegistryExport.FileName)}");
            sb.AppendLine($"echo To restore it, right-click the .reg file and select \"Merge\".");
            sb.AppendLine($"echo This script does NOT automatically import registry keys.");
            sb.AppendLine();
        }

        sb.AppendLine("echo.");
        sb.AppendLine("if %ERRORS% EQU 0 (");
        sb.AppendLine("    echo Restore complete! All files have been moved back.");
        sb.AppendLine(") else (");
        sb.AppendLine("    echo Restore completed with %ERRORS% error(s). Check the output above.");
        sb.AppendLine(")");
        sb.AppendLine("echo.");
        sb.AppendLine("pause");

        return sb.ToString();
    }
}
