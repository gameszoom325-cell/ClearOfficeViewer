using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClearOfficeViewer.Core.Services;

public sealed record FileAssociationSpec(string Extension, string ProgId, string Description);

public static class FileAssociationService
{
    public const string ApplicationName = "ClearOffice Viewer";
    public const string ExecutableFileName = "ClearOfficeViewer.exe";
    private const string CapabilitiesSubKey = @"Software\ClearOfficeViewer\Capabilities";
    private const string RegisteredAppsSubKey = @"Software\RegisteredApplications";

    public static readonly IReadOnlyList<FileAssociationSpec> SupportedAssociations =
    [
        new(".pdf",  "ClearOfficeViewer.Pdf",  "ClearOffice PDF Document"),
        new(".doc",  "ClearOfficeViewer.Doc",  "ClearOffice Word 97-2003 Document"),
        new(".docx", "ClearOfficeViewer.Docx", "ClearOffice Word Document"),
        new(".ppt",  "ClearOfficeViewer.Ppt",  "ClearOffice PowerPoint 97-2003 Presentation"),
        new(".pptx", "ClearOfficeViewer.Pptx", "ClearOffice PowerPoint Presentation"),
        new(".html", "ClearOfficeViewer.Html", "ClearOffice HTML Web Document"),
        new(".htm",  "ClearOfficeViewer.Htm",  "ClearOffice HTML Web Document")
    ];

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static string GetOpenCommand(string executablePath) => $"\"{executablePath}\" \"%1\"";

    public static bool IsRegisteredForCurrentUser()
    {
        try
        {
            using var appKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Applications\{ExecutableFileName}\shell\open\command");
            return appKey?.GetValue(null) is string cmd && !string.IsNullOrWhiteSpace(cmd);
        }
        catch
        {
            return false;
        }
    }

    public static void RegisterForCurrentUser(string? executablePath = null)
    {
        string exePath = executablePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ExecutableFileName);
        if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            exePath = Path.ChangeExtension(exePath, ".exe");

        string openCommand = GetOpenCommand(exePath);
        string iconValue = $"\"{exePath}\",0";

        // 1. Register Applications\<exe> so "Open with -> Choose another app" lists ClearOffice Viewer
        using (var appBase = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ExecutableFileName}"))
        {
            appBase.SetValue("FriendlyAppName", ApplicationName);
            using var iconKey = appBase.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, iconValue);

            using var cmdKey = appBase.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue(null, openCommand);

            using var supportedKey = appBase.CreateSubKey("SupportedTypes");
            foreach (var spec in SupportedAssociations)
            {
                supportedKey.SetValue(spec.Extension, string.Empty);
            }
        }

        // 2. Register ProgIDs and extension OpenWithProgids
        foreach (var spec in SupportedAssociations)
        {
            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{spec.ProgId}"))
            {
                progIdKey.SetValue(null, spec.Description);
                progIdKey.SetValue("FriendlyTypeName", spec.Description);

                using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue(null, iconValue);

                using var cmdKey = progIdKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue(null, openCommand);
            }

            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{spec.Extension}\OpenWithProgids");
            extKey.SetValue(spec.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // 3. Register Capabilities and RegisteredApplications for Windows Default Apps UI
        using (var capKey = Registry.CurrentUser.CreateSubKey(CapabilitiesSubKey))
        {
            capKey.SetValue("ApplicationName", ApplicationName);
            capKey.SetValue("ApplicationDescription", "Lightweight native viewer for PDF, Word, PowerPoint, and HTML files.");

            using var faKey = capKey.CreateSubKey("FileAssociations");
            foreach (var spec in SupportedAssociations)
            {
                faKey.SetValue(spec.Extension, spec.ProgId);
            }
        }

        using (var regApps = Registry.CurrentUser.CreateSubKey(RegisteredAppsSubKey))
        {
            regApps.SetValue(ApplicationName, CapabilitiesSubKey);
        }

        NotifyShellAssociationChanged();
        AppLogger.Instance.Info(nameof(FileAssociationService), $"Registered HKCU file associations for '{exePath}'.");
    }

    public static void UnregisterForCurrentUser()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{ExecutableFileName}", throwOnMissingSubKey: false);

            foreach (var spec in SupportedAssociations)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{spec.ProgId}", throwOnMissingSubKey: false);
                using var openWithKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{spec.Extension}\OpenWithProgids", writable: true);
                openWithKey?.DeleteValue(spec.ProgId, throwOnMissingValue: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\ClearOfficeViewer", throwOnMissingSubKey: false);
            using var regApps = Registry.CurrentUser.OpenSubKey(RegisteredAppsSubKey, writable: true);
            regApps?.DeleteValue(ApplicationName, throwOnMissingValue: false);

            NotifyShellAssociationChanged();
            AppLogger.Instance.Info(nameof(FileAssociationService), "Removed HKCU file associations.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(nameof(FileAssociationService), "Failed to unregister HKCU file associations.", ex);
            throw;
        }
    }

    public static void OpenWindowsDefaultAppsSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    public static void NotifyShellAssociationChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Ignore in headless test environments.
        }
    }
}
