using VaporVault.Core.Data;
using VaporVault.Core.Models;

namespace VaporVault.Core.Quarantine;

/// <summary>
/// Checks whether a new quarantine operation would exceed the vault storage cap.
///
/// This is a standalone, testable module. It does NOT auto-purge —
/// it returns information for the UI to show a dialog and let the user decide.
///
/// Usage:
///   var enforcer = new VaultCapEnforcer(quarantineIndex);
///   var result = enforcer.CheckCap(incomingSizeBytes);
///   if (result.WouldExceedCap) { /* show dialog */ }
/// </summary>
public class VaultCapEnforcer
{
    private readonly QuarantineIndex _quarantineIndex;

    public VaultCapEnforcer(QuarantineIndex quarantineIndex)
    {
        _quarantineIndex = quarantineIndex;
    }

    /// <summary>
    /// Result of a vault cap check.
    /// </summary>
    /// <param name="WouldExceedCap">True if adding the incoming app would exceed the cap.</param>
    /// <param name="CurrentUsageBytes">Total size of all currently active quarantine entries.</param>
    /// <param name="CapBytes">The configured cap from settings.</param>
    /// <param name="IncomingSizeBytes">Size of the app about to be quarantined.</param>
    /// <param name="OldestEntry">The oldest active entry that could be deleted to free space. Null if vault is empty.</param>
    /// <param name="SpaceNeededBytes">How many bytes over the cap. 0 if not over.</param>
    public record CapCheckResult(
        bool WouldExceedCap,
        long CurrentUsageBytes,
        long CapBytes,
        long IncomingSizeBytes,
        QuarantineEntry? OldestEntry,
        long SpaceNeededBytes);

    /// <summary>
    /// Checks whether quarantining an app of the given size would exceed the vault cap.
    /// </summary>
    /// <param name="incomingSizeBytes">Size of the app about to be quarantined.</param>
    /// <returns>Cap check result with all information needed for the UI dialog.</returns>
    public CapCheckResult CheckCap(long incomingSizeBytes)
    {
        var settings = AppSettings.Load();
        var cap = settings.MaxQuarantineSizeBytes;

        // Cap of 0 means disabled
        if (cap <= 0)
        {
            return new CapCheckResult(
                WouldExceedCap: false,
                CurrentUsageBytes: 0,
                CapBytes: 0,
                IncomingSizeBytes: incomingSizeBytes,
                OldestEntry: null,
                SpaceNeededBytes: 0);
        }

        var activeEntries = _quarantineIndex.GetActive();
        var currentUsage = activeEntries.Sum(e => e.TotalSizeBytes);
        var projectedUsage = currentUsage + incomingSizeBytes;

        if (projectedUsage <= cap)
        {
            return new CapCheckResult(
                WouldExceedCap: false,
                CurrentUsageBytes: currentUsage,
                CapBytes: cap,
                IncomingSizeBytes: incomingSizeBytes,
                OldestEntry: null,
                SpaceNeededBytes: 0);
        }

        // Would exceed cap — find the oldest active entry
        var oldest = activeEntries
            .OrderBy(e => e.QuarantinedAt)
            .FirstOrDefault();

        return new CapCheckResult(
            WouldExceedCap: true,
            CurrentUsageBytes: currentUsage,
            CapBytes: cap,
            IncomingSizeBytes: incomingSizeBytes,
            OldestEntry: oldest,
            SpaceNeededBytes: projectedUsage - cap);
    }
}
