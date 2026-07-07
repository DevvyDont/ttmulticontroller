using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TTMulti
{
    /// <summary>
    /// Downloads a new single-file exe from a GitHub release and applies it in place, then relaunches. A running
    /// exe cannot be overwritten, but Windows does allow renaming it, so the swap is: rename the running exe to
    /// "...old.exe", move the downloaded exe into its place, start the new one, and shut down. The leftover
    /// "...old.exe" is deleted on the next launch (<see cref="CleanupOldExe"/>).
    /// </summary>
    internal static class SelfUpdater
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            // No overall timeout: a large download is bounded by the CancellationToken instead. Headers are still
            // read first (ResponseHeadersRead), so a stall before the body arrives is caught by the token.
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Multicontroller");
            return client;
        }

        private static string SiblingExe(string suffix) => AppPaths.InExeDirectory(
            Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath) + suffix + ".exe");

        private static string OldExePath => SiblingExe(".old");
        private static string NewExePath => SiblingExe(".new");

        /// <summary>Best-effort delete of a leftover "...old.exe" from a previous update. Call once at startup;
        /// swallows errors (the just-exited old process may still hold the lock, in which case the next launch
        /// clears it).</summary>
        internal static void CleanupOldExe()
        {
            try { if (File.Exists(OldExePath)) File.Delete(OldExePath); }
            catch { /* still locked; retried next launch */ }
        }

        /// <summary>True when the exe's folder is writable (a normal portable install). False for a protected
        /// location (e.g. Program Files), where the in-place swap would need elevation and the caller should fall
        /// back to the manual browser download instead of half-applying.</summary>
        internal static bool CanWriteToExeDir()
        {
            try
            {
                string probe = AppPaths.InExeDirectory(".mc-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Download the new exe beside the current one, reporting 0..1 progress. Returns the temp file path
        /// on success; throws on failure or cancellation (the partial file is cleaned up).</summary>
        internal static async Task<string> DownloadAsync(string url, IProgress<double> progress, CancellationToken ct)
        {
            string dest = NewExePath;
            try
            {
                using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;

                    using (var src = await resp.Content.ReadAsStreamAsync(ct))
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long read = 0;
                        int n;
                        while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            await dst.WriteAsync(buffer, 0, n, ct);
                            read += n;
                            if (total.HasValue && total.Value > 0)
                                progress?.Report((double)read / total.Value);
                        }
                        if (total.HasValue && read != total.Value)
                            throw new IOException($"Incomplete download ({read} of {total.Value} bytes).");
                    }
                }

                ValidatePeFile(dest);
                return dest;
            }
            catch
            {
                TryDelete(dest);
                throw;
            }
        }

        /// <summary>Swap the downloaded exe in for the running one and relaunch. Does not return on success (the app
        /// shuts down). On failure it restores the original exe so the app is never left without one, then throws.</summary>
        internal static void ApplyAndRestart(string newExePath)
        {
            string current = AppPaths.ExecutablePath;
            string old = OldExePath;

            TryDelete(old);           // clear any stale leftover first
            File.Move(current, old);  // rename the running exe (permitted even while running)
            try
            {
                File.Move(newExePath, current);
            }
            catch
            {
                // Put the original back so we never leave the app without its exe.
                try { if (!File.Exists(current)) File.Move(old, current); } catch { /* nothing more we can do */ }
                throw;
            }

            // Launch the new exe (no elevation verb: the new instance re-elevates itself via its own setting if
            // needed), then shut down cleanly so input hooks are uninstalled via MainWindow.OnClosing.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(current) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }

        private static void ValidatePeFile(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 1024)
                throw new IOException("The downloaded file is missing or too small.");

            using (var fs = File.OpenRead(path))
            {
                // Every Windows PE starts with "MZ".
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                    throw new IOException("The downloaded file is not a valid Windows executable.");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }
}
