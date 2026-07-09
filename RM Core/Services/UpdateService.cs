using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RM_Core.Services
{
    /// <summary>
    /// Checks GitHub Releases API for a newer version of RM Core.
    /// </summary>
    public class UpdateService
    {
        // Configure o owner/repo no settings do aplicativo antes de publicar
        private const string RepoUrl = "https://api.github.com/repos/senamiguel/RM-Core/releases/latest";
        private const string CurrentVersion = "1.0.0";

        /// <summary>
        /// Returns <see cref="UpdateInfo"/> when a newer version is available; otherwise null.
        /// Never throws — network errors are swallowed silently.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdates()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RM-Core/1.0");

            try
            {
                var response = await client.GetStringAsync(RepoUrl);
                var options  = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var release  = JsonSerializer.Deserialize<GitHubRelease>(response, options);

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    return null;

                if (CompareVersions(release.TagName, CurrentVersion) > 0)
                {
                    return new UpdateInfo
                    {
                        Version     = release.TagName,
                        DownloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl ?? string.Empty,
                        Changelog   = release.Body ?? string.Empty
                    };
                }
            }
            catch
            {
                // No internet, invalid JSON, or repository not yet created — silently return null.
            }

            return null;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Semantic version comparison (handles optional leading "v").
        /// Returns positive when v1 &gt; v2, negative when v1 &lt; v2, zero when equal.
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            static int[] Parse(string v)
            {
                return v.TrimStart('v')
                        .Split('.')
                        .Select(p => int.TryParse(p, out var n) ? n : 0)
                        .ToArray();
            }

            var p1 = Parse(v1);
            var p2 = Parse(v2);
            int len = Math.Min(p1.Length, p2.Length);

            for (int i = 0; i < len; i++)
            {
                if (p1[i] != p2[i])
                    return p1[i].CompareTo(p2[i]);
            }

            return p1.Length.CompareTo(p2.Length);
        }
    }

    // ---------------------------------------------------------------
    // DTOs
    // ---------------------------------------------------------------

    public class UpdateInfo
    {
        public string Version     { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Changelog   { get; set; } = string.Empty;
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
