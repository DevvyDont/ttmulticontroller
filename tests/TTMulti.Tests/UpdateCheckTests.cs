using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the pure logic behind the in-app update check: deriving the GitHub releases API URL from the
    /// configured homepage, tolerant version parsing, and the newer-than comparison.
    /// </summary>
    public class UpdateCheckTests
    {
        [Theory]
        [InlineData("https://github.com/owner/repo", "https://api.github.com/repos/owner/repo/releases/latest")]
        [InlineData("https://github.com/owner/repo/", "https://api.github.com/repos/owner/repo/releases/latest")]
        [InlineData("https://GitHub.com/Owner/Repo", "https://api.github.com/repos/Owner/Repo/releases/latest")]
        public void BuildReleasesApiUrl_maps_github_homepage(string homepage, string expected)
        {
            Assert.Equal(expected, UpdateChecker.BuildReleasesApiUrl(homepage));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("https://example.com/owner/repo")]  // not github.com
        [InlineData("https://github.com/owner")]        // missing repo segment
        [InlineData("https://github.com/owner/repo/extra")] // too many segments
        public void BuildReleasesApiUrl_rejects_non_release_urls(string homepage)
        {
            Assert.Null(UpdateChecker.BuildReleasesApiUrl(homepage));
        }

        [Theory]
        [InlineData("1.4.0", 1, 4, 0)]
        [InlineData("v1.4.0", 1, 4, 0)]
        [InlineData("V2.0", 2, 0, -1)]
        [InlineData("v1.4.0-beta", 1, 4, 0)]
        [InlineData("  v1.2.3  ", 1, 2, 3)]
        public void ParseVersion_is_tolerant(string text, int major, int minor, int build)
        {
            var v = UpdateChecker.ParseVersion(text);
            Assert.NotNull(v);
            Assert.Equal(major, v.Major);
            Assert.Equal(minor, v.Minor);
            Assert.Equal(build, v.Build);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-version")]
        public void ParseVersion_returns_null_for_garbage(string text)
        {
            Assert.Null(UpdateChecker.ParseVersion(text));
        }

        [Theory]
        [InlineData("v1.4.0", "1.3.0.0", true)]   // newer release available
        [InlineData("v1.3.0", "1.4.0.0", false)]  // installed is newer
        [InlineData("v1.4.0", "1.4.0.0", false)]  // same version
        public void IsNewerVersion_compares_as_versions(string latest, string current, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewerVersion(latest, current));
        }

        [Fact]
        public void IsNewerVersion_falls_back_to_string_diff_when_unparseable()
        {
            // Neither side parses as a Version -> "different means newer" fallback.
            Assert.True(UpdateChecker.IsNewerVersion("nightly-b", "nightly-a"));
            Assert.False(UpdateChecker.IsNewerVersion("nightly-a", "nightly-a"));
        }

        [Fact]
        public void PickExeAssetUrl_prefers_the_exact_named_asset()
        {
            string[] names = { "notes.txt", "Multicontroller.exe", "other.exe" };
            string[] urls = { "u/notes", "u/mc", "u/other" };
            Assert.Equal("u/mc", UpdateChecker.PickExeAssetUrl("Multicontroller.exe", names, urls));
        }

        [Fact]
        public void PickExeAssetUrl_matches_the_name_case_insensitively()
        {
            string[] names = { "multicontroller.EXE" };
            string[] urls = { "u/mc" };
            Assert.Equal("u/mc", UpdateChecker.PickExeAssetUrl("Multicontroller.exe", names, urls));
        }

        [Fact]
        public void PickExeAssetUrl_falls_back_to_the_first_exe_when_no_exact_match()
        {
            string[] names = { "readme.md", "SomethingElse.exe", "extra.exe" };
            string[] urls = { "u/readme", "u/first", "u/second" };
            Assert.Equal("u/first", UpdateChecker.PickExeAssetUrl("Multicontroller.exe", names, urls));
        }

        [Fact]
        public void PickExeAssetUrl_returns_null_when_there_is_no_exe_asset()
        {
            string[] names = { "readme.md", "notes.txt" };
            string[] urls = { "u/readme", "u/notes" };
            Assert.Null(UpdateChecker.PickExeAssetUrl("Multicontroller.exe", names, urls));
        }

        [Fact]
        public void PickExeAssetUrl_returns_null_for_empty_or_null_input()
        {
            Assert.Null(UpdateChecker.PickExeAssetUrl("Multicontroller.exe", null, null));
            Assert.Null(UpdateChecker.PickExeAssetUrl("Multicontroller.exe", new string[0], new string[0]));
        }
    }
}
