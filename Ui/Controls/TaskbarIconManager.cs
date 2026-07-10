using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// Themes the pinned taskbar icon to match the user's Multi/Mirror colours. Windows takes a pinned shortcut's
    /// icon from the shortcut file (which normally resolves to the exe's baked-in ApplicationIcon), not from the
    /// running window, so recolouring the live Window.Icon alone leaves the pinned icon on the default palette.
    /// This renders a per-user .ico in the user's colours to a stable path and points any pinned-taskbar shortcut
    /// that targets this exe at it, then asks the shell to refresh.
    ///
    /// Best-effort and fully guarded: shell icon caching means a change may only show after the user unpins and
    /// re-pins once (or restarts Explorer), and it only affects the current machine. Never throws to callers.
    /// </summary>
    internal static class TaskbarIconManager
    {
        private static System.Drawing.Color _lastLeft, _lastRight;
        private static bool _rendered;

        /// <summary>
        /// Render the per-user icon (only when the colours changed or the file is missing) and repoint any pinned
        /// taskbar shortcut for this exe at it. Call on the UI thread; safe to call repeatedly (it no-ops when
        /// nothing changed). COM (IShellLink) requires STA, which the WPF UI thread provides.
        /// </summary>
        internal static void Refresh()
        {
            try
            {
                string exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    return;

                string icoPath = UserIconPath();
                Directory.CreateDirectory(Path.GetDirectoryName(icoPath));

                bool colorsChanged = !_rendered
                    || _lastLeft != Colors.LeftGroup || _lastRight != Colors.AllGroups;

                if (colorsChanged || !File.Exists(icoPath))
                {
                    IconExporter.SaveUserIco(icoPath);
                    _lastLeft = Colors.LeftGroup;
                    _lastRight = Colors.AllGroups;
                    _rendered = true;
                }

                bool shortcutChanged = RepointPinnedShortcuts(exePath, icoPath);

                // Nudge the shell to re-read icons when either the file content or a shortcut changed.
                if (colorsChanged || shortcutChanged)
                    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("TaskbarIconManager.Refresh failed: " + ex);
            }
        }

        private static string UserIconPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Multicontroller", "app-icon.ico");

        /// <summary>Point every pinned-taskbar shortcut that targets this exe at <paramref name="icoPath"/>.
        /// Returns true if any shortcut was changed.</summary>
        private static bool RepointPinnedShortcuts(string exePath, string icoPath)
        {
            string pinnedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

            if (!Directory.Exists(pinnedDir))
                return false;

            string exeFull = SafeFullPath(exePath);
            bool changed = false;

            foreach (string lnk in Directory.EnumerateFiles(pinnedDir, "*.lnk"))
            {
                IShellLinkW link = null;
                try
                {
                    link = (IShellLinkW)new ShellLink();
                    var persist = (IPersistFile)link;
                    persist.Load(lnk, STGM_READWRITE);

                    var target = new StringBuilder(MAX_PATH);
                    link.GetPath(target, target.Capacity, IntPtr.Zero, SLGP_RAWPATH);
                    if (!string.Equals(SafeFullPath(target.ToString()), exeFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var curIcon = new StringBuilder(MAX_PATH);
                    link.GetIconLocation(curIcon, curIcon.Capacity, out int curIndex);
                    if (curIndex == 0 && string.Equals(curIcon.ToString(), icoPath, StringComparison.OrdinalIgnoreCase))
                        continue; // already pointing at our icon

                    link.SetIconLocation(icoPath, 0);
                    persist.Save(lnk, true);
                    changed = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("TaskbarIconManager: could not update " + lnk + ": " + ex.Message);
                }
                finally
                {
                    if (link != null)
                        Marshal.FinalReleaseComObject(link);
                }
            }

            return changed;
        }

        private static string SafeFullPath(string p)
        {
            try { return string.IsNullOrEmpty(p) ? p : Path.GetFullPath(p); }
            catch { return p; }
        }

        // ── Shell interop ───────────────────────────────────────────────────────────

        private const int MAX_PATH = 260;
        private const int STGM_READWRITE = 0x00000002;
        private const int SLGP_RAWPATH = 0x0004;
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
