using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Abstraction for detecting additional app traces (scheduled tasks, services,
/// file associations, dead uninstall entries) for a given app name.
/// Supports unit testing via dependency injection.
/// </summary>
public interface IAppTraceDetector
{
    /// <summary>
    /// Detects additional traces left by an app. Read-only — no side effects.
    /// </summary>
    /// <param name="appName">Display name of the app to search for.</param>
    /// <param name="installLocation">Optional install location path for more precise matching.</param>
    /// <returns>Result containing all detected traces across all categories.</returns>
    AppTraceResult DetectTraces(string appName, string? installLocation = null);
}
