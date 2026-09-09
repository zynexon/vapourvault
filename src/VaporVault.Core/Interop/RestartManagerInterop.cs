using System.Runtime.InteropServices;

namespace VaporVault.Core.Interop;

/// <summary>
/// P/Invoke declarations for the Windows Restart Manager API (Rstrtmgr.dll).
/// Used to detect which processes have open file handles in a target directory
/// before attempting to move files during quarantine.
/// </summary>
internal static class RestartManagerInterop
{
    /// <summary>Maximum length of a session key string.</summary>
    private const int CCH_RM_SESSION_KEY = 256;

    /// <summary>Maximum number of processes that can be returned.</summary>
    private const int RM_MAX_PROCESSES = 128;

    // RM_APP_TYPE enum
    internal const int RmUnknownApp = 0;
    internal const int RmMainWindow = 1;
    internal const int RmOtherWindow = 2;
    internal const int RmService = 3;
    internal const int RmExplorer = 4;
    internal const int RmConsole = 5;
    internal const int RmCritical = 1000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_SESSION_KEY + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_SESSION_KEY + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    /// <summary>
    /// Starts a new Restart Manager session.
    /// </summary>
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(
        out uint pSessionHandle,
        int dwSessionFlags,
        string strSessionKey);

    /// <summary>
    /// Registers resources (files, processes, services) with the Restart Manager session.
    /// </summary>
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    /// <summary>
    /// Gets the list of processes that are using the registered resources.
    /// </summary>
    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    /// <summary>
    /// Shuts down processes that are using the registered resources.
    /// </summary>
    [DllImport("rstrtmgr.dll")]
    internal static extern int RmShutdown(
        uint pSessionHandle,
        int lActionFlags,
        IntPtr fnStatus);

    /// <summary>
    /// Ends the Restart Manager session and releases resources.
    /// </summary>
    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint pSessionHandle);

    /// <summary>
    /// Finds all processes that have file handles open in any of the specified file paths.
    /// </summary>
    /// <param name="filePaths">Full paths to files to check.</param>
    /// <returns>List of process info structs for processes holding handles.</returns>
    internal static List<RM_PROCESS_INFO> GetLockingProcesses(string[] filePaths)
    {
        var result = new List<RM_PROCESS_INFO>();

        string sessionKey = Guid.NewGuid().ToString();
        int error = RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (error != 0) return result;

        try
        {
            error = RmRegisterResources(sessionHandle,
                (uint)filePaths.Length, filePaths,
                0, null,
                0, null);

            if (error != 0) return result;

            uint pnProcInfo = 0;
            uint lpdwRebootReasons = 0;

            // First call to get the count
            error = RmGetList(sessionHandle, out uint pnProcInfoNeeded,
                ref pnProcInfo, null, ref lpdwRebootReasons);

            if (error == 0 || pnProcInfoNeeded == 0) return result;

            // Second call to get the actual data
            var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
            pnProcInfo = pnProcInfoNeeded;

            error = RmGetList(sessionHandle, out pnProcInfoNeeded,
                ref pnProcInfo, processInfo, ref lpdwRebootReasons);

            if (error == 0)
            {
                for (int i = 0; i < pnProcInfo; i++)
                {
                    result.Add(processInfo[i]);
                }
            }
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return result;
    }
}
