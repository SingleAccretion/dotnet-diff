using System.Collections.Generic;
using System.CommandLine;
using System.Text.Json;
using System.Threading;

namespace DotnetDiff
{
    public class DotnetReleases
    {
        private static DotnetReleases? _releases;

        private DotnetReleases(int? latestPreviewVersionNumber, DotnetRelease? nextRelease, DotnetRelease currentRelease, IReadOnlyList<DotnetRelease> supportedReleases)
        {
            LatestPreviewVersionNumber = latestPreviewVersionNumber;
            NextRelease = nextRelease;
            CurrentRelease = currentRelease;
            SupportedReleases = supportedReleases;
        }

        public int? LatestPreviewVersionNumber { get; }
        public DotnetRelease? NextRelease { get; }
        public DotnetRelease? CurrentRelease { get; }
        public IReadOnlyList<DotnetRelease> SupportedReleases { get; }

        public static DotnetReleases Resolve(IConsole console) => LazyInitializer.EnsureInitialized(ref _releases, () => DownloadReleases(console));

        private static DotnetReleases DownloadReleases(IConsole console)
        {
            var uri = "https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json";
            console.WriteLineDebug($"About to download releases-index.json");
            console.WriteLineDebug($"GET {uri}");
            var releasesJson = IO.RequestGet(uri);
            if (!releasesJson.IsSuccessStatusCode)
            {
                throw new UserException($"Failed to retrieve releases-index.json, server returned {releasesJson.StatusCode}");
            }

            List<DotnetRelease> supportedReleases = new List<DotnetRelease>();
            DotnetRelease? currentRelease = null;
            DotnetRelease? nextRelease = null;
            int? latestPreviewVersionNumber = null;

            var json = JsonDocument.Parse(releasesJson.Content.ReadAsStream()).RootElement;
            json = json.GetProperty("releases-index");
            foreach (var jsonRelease in json.EnumerateArray())
            {
                var shortVersion = jsonRelease.GetProperty("channel-version").GetString() ?? throw new UserException("Channel version is not present in the JSON for releases");
                var fullVersion = jsonRelease.GetProperty("latest-release").GetString() ?? throw new UserException("Version is not present in the JSON for releases");
                var supportPhase = jsonRelease.GetProperty("support-phase").GetString() ?? throw new UserException("Support phase is not present in the JSON for releases");
                var latestRuntimeVersion = jsonRelease.GetProperty("latest-runtime").GetString() ?? throw new UserException("Latest runtime version is not present in the JSON for releases");
                switch (supportPhase)
                {
                    case "preview":
                        latestPreviewVersionNumber = int.Parse(fullVersion[(fullVersion.IndexOf("-preview.") + "-preview.".Length)..]);
                        nextRelease = new(shortVersion, supportPhase, latestRuntimeVersion);
                        break;

                    case "sts":
                    case "lts":
                    case "current":
                        // The supported releases are ordered from most recent to least recent. Take the first one as "current".
                        currentRelease ??= new(shortVersion, supportPhase, latestRuntimeVersion);
                        supportedReleases.Add(new(shortVersion, supportPhase, latestRuntimeVersion));
                        break;

                    default:
                        break;
                }
            }

            if (currentRelease is null)
            {
                throw new UserException($"Could not find \"current\" release in the JSON for releases");
            }

            return new(latestPreviewVersionNumber, nextRelease, currentRelease, supportedReleases);
        }
    }

    public record DotnetRelease(string ShortVersion, string SupportPhase, string LatestRuntimeFullVersion);
}
