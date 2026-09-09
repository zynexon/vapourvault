using VaporVault.Core.Models;

namespace VaporVault.Core.Detection;

/// <summary>
/// Enumerates top-level subdirectories in the standard app data locations:
/// %LocalAppData%, %AppData% (Roaming), and %ProgramData%.
/// </summary>
public class AppDataFolderEnumerator : IAppDataFolderEnumerator
{
    /// <summary>
    /// Enumerates all top-level folders in the three app data locations.
    /// Only returns folders (not files) at the top level.
    /// </summary>
    public IReadOnlyList<AppDataFolder> EnumerateFolders()
    {
        var folders = new List<AppDataFolder>();

        EnumerateLocation(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataLocation.LocalAppData,
            folders);

        EnumerateLocation(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataLocation.RoamingAppData,
            folders);

        EnumerateLocation(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppDataLocation.ProgramData,
            folders);

        return folders;
    }

    private static void EnumerateLocation(string basePath, AppDataLocation location, List<AppDataFolder> results)
    {
        if (!Directory.Exists(basePath)) return;

        try
        {
            foreach (var dirPath in Directory.EnumerateDirectories(basePath))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dirPath);
                    var folder = new AppDataFolder
                    {
                        FullPath = dirPath,
                        Name = dirInfo.Name,
                        Location = location
                    };

                    // Calculate size and last write time
                    // Use a try-catch per folder since some may be access-denied
                    try
                    {
                        long totalSize = 0;
                        var lastWrite = DateTime.MinValue;

                        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                totalSize += file.Length;
                                if (file.LastWriteTimeUtc > lastWrite)
                                    lastWrite = file.LastWriteTimeUtc;
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }

                        folder.SizeBytes = totalSize;
                        folder.LastWriteTimeUtc = lastWrite == DateTime.MinValue
                            ? dirInfo.LastWriteTimeUtc
                            : lastWrite;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Can't enumerate files — use directory-level info
                        folder.SizeBytes = 0;
                        folder.LastWriteTimeUtc = dirInfo.LastWriteTimeUtc;
                    }

                    results.Add(folder);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

/// <summary>
/// Abstraction for folder enumeration, to support unit testing.
/// </summary>
public interface IAppDataFolderEnumerator
{
    IReadOnlyList<AppDataFolder> EnumerateFolders();
}
