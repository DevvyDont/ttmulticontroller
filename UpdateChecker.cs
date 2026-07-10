using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TTMulti
{
    internal enum UpdateStatus
    {
        /// <summary>Couldn't reach GitHub / parse the response.</summary>
        CheckFailed,
        /// <summary>Installed version is current.</summary>
        UpToDate,
        /// <summary>A newer release exists.</summary>
        UpdateAvailable,
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateStatus Status { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestTag { get; set; }
        public string DownloadUrl { get; set; }
        /// <summary>Direct download URL of the release's exe asset (for the in-app self-updater); null if none.</summary>
        public string AssetDownloadUrl { get; set; }
    }

    /// <summary>
    /// Queries the GitHub "latest release" API for the repo named by the homepage setting and compares it to the
    /// installed version. Extracted from the old OptionsDlg so the update check survives that dialog's removal.
    /// </summary>
    internal static class UpdateChecker
    {
        // One shared HttpClient (avoids socket exhaustion). GitHub requires a User-Agent on every request.
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Multicontroller");
            return client;
        }

        internal static string CurrentVersion => System.Windows.Forms.Application.ProductVersion;

        public static async Task<UpdateCheckResult> CheckAsync()
        {
            string current = CurrentVersion;
            GitHubRelease release = await FetchLatestReleaseAsync();

            if (release == null || string.IsNullOrEmpty(release.TagName))
                return new UpdateCheckResult { Status = UpdateStatus.CheckFailed, CurrentVersion = current };

            if (IsNewerVersion(release.TagName, current))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.UpdateAvailable,
                    CurrentVersion = current,
                    LatestTag = release.TagName,
                    DownloadUrl = !string.IsNullOrEmpty(release.HtmlUrl)
                        ? release.HtmlUrl
                        : Properties.Settings.Default.homepageUrl,
                    AssetDownloadUrl = SelectExeAssetUrl(release),
                };
            }

            return new UpdateCheckResult { Status = UpdateStatus.UpToDate, CurrentVersion = current };
        }

        /// <summary>The download URL of the release's exe asset that matches the running exe name (falling back to
        /// any .exe asset), or null. Split into the pure <see cref="PickExeAssetUrl"/> so it can be unit-tested.</summary>
        private static string SelectExeAssetUrl(GitHubRelease release)
        {
            if (release?.Assets == null)
                return null;

            var names = new string[release.Assets.Length];
            var urls = new string[release.Assets.Length];
            for (int i = 0; i < release.Assets.Length; i++)
            {
                names[i] = release.Assets[i]?.Name;
                urls[i] = release.Assets[i]?.BrowserDownloadUrl;
            }
            return PickExeAssetUrl(System.IO.Path.GetFileName(AppPaths.ExecutablePath), names, urls);
        }

        /// <summary>Pick the download URL for the asset named <paramref name="expectedName"/> (case-insensitive),
        /// falling back to the first asset ending in ".exe". Returns null when there is no exe asset. Pure/testable.</summary>
        internal static string PickExeAssetUrl(string expectedName, string[] assetNames, string[] assetUrls)
        {
            if (assetNames == null || assetUrls == null)
                return null;

            for (int i = 0; i < assetNames.Length; i++)
                if (i < assetUrls.Length && string.Equals(assetNames[i], expectedName, StringComparison.OrdinalIgnoreCase))
                    return assetUrls[i];

            for (int i = 0; i < assetNames.Length; i++)
                if (i < assetUrls.Length && assetNames[i] != null
                    && assetNames[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return assetUrls[i];

            return null;
        }

        private static async Task<GitHubRelease> FetchLatestReleaseAsync()
        {
            string apiUrl = BuildReleasesApiUrl(Properties.Settings.Default.homepageUrl);
            if (apiUrl == null)
                return null;

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                {
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using (var response = await Http.SendAsync(request, cts.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;
                        return ParseRelease(await response.Content.ReadAsStringAsync());
                    }
                }
            }
            catch
            {
                // No network, 404, DNS failure, timeout, malformed response — treated as "can't check".
                return null;
            }
        }

        private static GitHubRelease ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (GitHubRelease)serializer.ReadObject(ms);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Turn https://github.com/owner/repo into the latest-release API endpoint (null if not a GitHub repo URL).</summary>
        internal static string BuildReleasesApiUrl(string homepageUrl)
        {
            if (string.IsNullOrWhiteSpace(homepageUrl))
                return null;

            const string prefix = "https://github.com/";
            string trimmed = homepageUrl.Trim().TrimEnd('/');
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string ownerRepo = trimmed.Substring(prefix.Length);
            if (ownerRepo.Split('/').Length != 2)
                return null;

            return "https://api.github.com/repos/" + ownerRepo + "/releases/latest";
        }

        internal static bool IsNewerVersion(string latestTag, string current)
        {
            Version latest = ParseVersion(latestTag);
            Version installed = ParseVersion(current);

            if (latest != null && installed != null)
                return latest > installed;

            // If either side can't be parsed, fall back to a conservative "different means newer".
            return !string.Equals((latestTag ?? "").Trim(), (current ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Accept tags like "v1.4", "1.4.0", "1.4.0-beta": strip a leading v and any non-version suffix.
            string trimmed = text.Trim().TrimStart('v', 'V');
            int end = 0;
            while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] == '.'))
                end++;
            trimmed = trimmed.Substring(0, end);

            return Version.TryParse(trimmed, out Version v) ? v : null;
        }

        [DataContract]
        private class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }

            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }

            [DataMember(Name = "assets")]
            public GitHubAsset[] Assets { get; set; }
        }

        [DataContract]
        private class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
