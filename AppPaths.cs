using System;
using System.IO;

namespace TTMulti
{
    /// <summary>
    /// Single source of truth for the directory the app resolves its portable data from (user.config,
    /// custom-modes.json, layout-presets.json). All three settings stores share this so they can never
    /// disagree about where "next to the exe" is.
    ///
    /// <para><see cref="System.Windows.Forms.Application.ExecutablePath"/> is the primary source because it
    /// returns the real host executable path on BOTH .NET Framework and modern .NET single-file bundles,
    /// where <c>Assembly.Location</c> is empty. <see cref="AppContext.BaseDirectory"/> is the fallback.</para>
    /// </summary>
    internal static class AppPaths
    {
        /// <summary>Full path to the running executable.</summary>
        internal static string ExecutablePath
        {
            get
            {
                try
                {
                    string path = System.Windows.Forms.Application.ExecutablePath;
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
                catch { }
                return Path.Combine(BaseDirectory, "ToontownMulticontroller.exe");
            }
        }

        /// <summary>Directory the executable lives in — where portable config files are read and written.</summary>
        internal static string ExeDirectory
        {
            get
            {
                try
                {
                    string path = System.Windows.Forms.Application.ExecutablePath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        string dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir))
                            return dir;
                    }
                }
                catch { }
                return BaseDirectory;
            }
        }

        /// <summary>Resolves a file name to a full path in <see cref="ExeDirectory"/>.</summary>
        internal static string InExeDirectory(string fileName) => Path.Combine(ExeDirectory, fileName);

        private static string BaseDirectory
        {
            get
            {
                string dir = AppContext.BaseDirectory;
                return string.IsNullOrEmpty(dir) ? "." : dir;
            }
        }
    }
}
