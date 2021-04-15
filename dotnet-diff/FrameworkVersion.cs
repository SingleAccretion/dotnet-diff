using System;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetDiff
{
    public sealed record FrameworkVersion
    {
        private const string PreviewSpecifier = "-preview.";

        // 6.0.0-preview.4.21205.3+b7a164882573af99eaf200c4b21808ecaf6dbb8c
        private static readonly Regex _versionFormat = new(@"\d+\.\d+\.\d+(-preview\.\d+\.\d+\.\d+)?\+[0-9a-f]{40}", RegexOptions.Compiled);

        private FrameworkVersion(string rawValue, int major, int minor, string moniker, string commitHash, string version, string release)
        {
            RawValue = rawValue;
            Major = major;
            Minor = minor;
            Moniker = moniker;
            CommitHash = commitHash;
            Version = version;
            Release = release;
        }

        public string RawValue { get; }

        public int Major { get; }
        public int Minor { get; }
        public string Moniker { get; }
        public string CommitHash { get; }
        public string Version { get; }
        public string Release { get; }

        public bool IsPreview => Version.Contains(PreviewSpecifier);

        public int GetPreviewNumber()
        {
            var version = Version.AsSpan();
            version = version[version.IndexOf(PreviewSpecifier)..];
            version = version[PreviewSpecifier.Length..];

            return int.Parse(version[..version.IndexOf('.')]);
        }

        public static FrameworkVersion ResolveLatest(IConsole console)
        {
            var releases = DotnetReleases.Resolve(console);
            
            var latest = releases.NextRelease;
            if (latest is null)
            {
                throw new UserException($"Failed to resolve the version for the latest release");
            }

            // These files are used in dotnet/installer for displaying the latest available installers.
            // They are ideal for our purposes.
            // The way we are getting the version _is not_ ideal, but hey, could have been worse!
            var uri = "https://raw.githubusercontent.com/dotnet/installer/main/eng/Version.Details.xml";

            console.WriteLineDebug("Resolving latest SDK version");
            console.WriteLineDebug($"GET {uri}");
            var deps = IO.RequestGet(uri);
            if (!deps.IsSuccessStatusCode)
            {
                throw new UserException($"Failed to retreive main/eng/Version.Details.xml from dotnet/installer, server returned {deps.StatusCode}");
            }
            using var reader = new StreamReader(deps.Content.ReadAsStream(), Encoding.UTF8);
            var xml = reader.ReadToEnd().AsSpan();
            
            var key = "<Dependency Name=\"Microsoft.NETCore.App.Runtime.win-x64\" Version=\"";
            xml = xml[(xml.IndexOf(key) + key.Length)..];

            var version = xml[..xml.IndexOf('"')].ToString();
            console.WriteLineDebug($"Resolved version: {version}");
            var commit = xml[(xml.IndexOf("<Sha>") + "<Sha>".Length)..xml.IndexOf("</Sha>")].ToString();
            console.WriteLineDebug($"Resolved commit: {commit}");

            return Parse($"{version}+{commit}");
        }

        public static FrameworkVersion Parse(string value)
        {
            if (!_versionFormat.IsMatch(value))
            {
                throw new FormatException($"Invalid dotnet version: '{value}'");
            }

            var rawValue = value;

            var span = value.AsSpan();
            var majorEnd = span.IndexOf('.');
            var major = int.Parse(span[..majorEnd]);

            if (major < 5)
            {
                throw new UserException($"Only versions starting with .NET 5 are supported");
            }

            span = span[(majorEnd + 1)..];
            var minor = int.Parse(span[..span.IndexOf('.')]);

            var moniker = $"net{major}.0";
            var release = $"{major}.{minor}";

            var plus = value.IndexOf('+');
            var commitHash = value[(plus + 1)..];
            var version = value[..plus];

            return new(value, major, minor, moniker, commitHash, version, release);
        }

        public bool Equals(FrameworkVersion? other) => other is not null && other.RawValue == RawValue;
        public override int GetHashCode() => RawValue.GetHashCode();

        public override string ToString() => $".NET {Version}, built from SHA {CommitHash}";
    }
}