using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using TTMulti.Forms;
using System.Xml.Serialization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TTMulti
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // DPI awareness (system-DPI) is declared in app.manifest, which the SDK build embeds as the native
            // Win32 manifest — so it applies before the first window is created (BUILD-03). SetHighDpiMode below is
            // a belt-and-suspenders no-op when the manifest wins the race, and covers the case where it doesn't.
            // Per-monitor-v2 is intentionally deferred to the UI stage (needs overlay/border coordinate rework).
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            // WinForms init is still needed for the kept overlays (BorderWnd / LayoutOverlayForm /
            // MonitorPickerForm), which coexist with the WPF UI on this STA thread. The Microsoft Sans Serif
            // pin keeps those overlays laid out as designed (modern .NET would default them to Segoe UI 9pt).
            Application.SetDefaultFont(new System.Drawing.Font("Microsoft Sans Serif", 8.25f));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Dev switch: render the cat logo to a multi-resolution .ico (the static exe/Explorer icon) and exit.
            // e.g. Multicontroller.exe --export-icon Resources\icon.ico
            if (args.Length >= 1 && string.Equals(args[0], "--export-icon", StringComparison.OrdinalIgnoreCase))
            {
                string icoPath = args.Length >= 2 ? args[1] : "Resources\\icon.ico";
                Ui.Controls.IconExporter.SaveIco(icoPath);
                return;
            }

            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }

            // Force save once at startup so user.config is created next to the exe when missing (portable settings).
            Properties.Settings.Default.Save();

            if (Properties.Settings.Default.runAsAdministrator)
            {
                if (args.Length == 0 || args[0] != "--runasadmin")
                {
                    if (TryRunAsAdmin())
                    {
                        return;
                    }
                }
            }

            WarnIfElevatedFromUserWritableLocation();

            RunWpfShell();
        }

        private static void RunWpfShell()
        {
            var app = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
            };

            // WPF-UI Fluent 2 resources, merged in code because there is no App.xaml — Program.Main stays
            // the entry point (elevation relaunch + portable-settings startup live here).
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

            // The engine marshals onto the UI thread through this adapter. Assign it before any controller
            // activity (the setter installs the WinEvent watcher).
            WindowWatcher.Instance.SynchronizingObject =
                new Threading.DispatcherSynchronizeInvoke(System.Windows.Threading.Dispatcher.CurrentDispatcher);

            var mainWindow = new Ui.MainWindow();
            app.Run(mainWindow);
        }

        /// <summary>
        /// Defense-in-depth: when running elevated, this process reads user.config / custom-modes.json /
        /// layout-presets.json from the exe directory. If that directory is writable by standard (non-admin) users
        /// — as it is for a portable install under the user profile — a non-administrator could tamper with that
        /// config to influence this elevated process (which installs system-wide keyboard/mouse hooks). Warn so the
        /// user can move the app to a protected location like Program Files. (SEC-01)
        /// </summary>
        private static void WarnIfElevatedFromUserWritableLocation()
        {
            try
            {
                if (!IsProcessElevated())
                    return;

                string dir = AppPaths.ExeDirectory;
                if (string.IsNullOrEmpty(dir) || !IsDirectoryWritableByStandardUsers(dir))
                    return;

                System.Windows.MessageBox.Show(
                    "This app is running as administrator from a folder that standard (non-administrator) users can " +
                    "modify:\n\n" + dir + "\n\n" +
                    "Its configuration files (user.config, custom-modes.json, layout-presets.json) are read by this " +
                    "elevated process, so a non-administrator could alter them to influence it. For better security, " +
                    "install the app to a protected location such as Program Files.",
                    "Running elevated from a writable location",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch
            {
                // Best-effort security notice; never block or crash startup over it.
            }
        }

        private static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDirectoryWritableByStandardUsers(string dir)
        {
            var nonAdminSids = new[]
            {
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),      // BUILTIN\Users
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), // Authenticated Users
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),            // Everyone
            };
            const FileSystemRights writeRights = FileSystemRights.WriteData | FileSystemRights.CreateFiles
                | FileSystemRights.AppendData | FileSystemRights.Modify | FileSystemRights.FullControl;

            DirectorySecurity security = new DirectoryInfo(dir).GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    continue;
                if ((rule.FileSystemRights & writeRights) == 0)
                    continue;
                if (rule.IdentityReference is SecurityIdentifier sid && nonAdminSids.Any(s => s.Equals(sid)))
                    return true;
            }
            return false;
        }

        internal static bool TryRunAsAdmin()
        {
            ProcessStartInfo processInfo = new ProcessStartInfo(AppPaths.ExecutablePath);
            processInfo.Arguments = "--runasadmin";
            processInfo.UseShellExecute = true;
            processInfo.Verb = "runas";

            try
            {
                Process.Start(processInfo);
                return true;
            }
            catch
            {
                Properties.Settings.Default.runAsAdministrator = false;
                Properties.Settings.Default.Save();
                return false;
            }
        }
    }
}
