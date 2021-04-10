using System;
using System.Text.RegularExpressions;

namespace DotnetDiff
{
    public sealed record FrameworkVersion
    {
        // 6.0.0-preview.4.21205.3+b7a164882573af99eaf200c4b21808ecaf6dbb8c
        private static readonly Regex _versionFormat = new(@"\d+\.\d+\.\d+(-preview\.\d+\.\d+\.\d+)?\+[0-9a-f]{40}", RegexOptions.Compiled);

        public FrameworkVersion(string value)
        {
            if (!_versionFormat.IsMatch(value))
            {
                throw new FormatException($"Invalid dotnet version: '{value}'");
            }

            RawValue = value;

            MajorDotnetVersion = int.Parse(value.AsSpan()[..value.IndexOf('.')]);
            Moniker = $"net{MajorDotnetVersion}.0";

            var plus = value.IndexOf('+');
            CommitHash = value[(plus + 1)..];
            Version = value[..plus];
        }

        public string RawValue { get; }

        public int MajorDotnetVersion { get; }
        public string Moniker { get; }
        public string CommitHash { get; }
        public string Version { get; }

        public static FrameworkVersion BestGuessSdkVersionForAssembly(string assemblyPath) => Sdk.SupportedSdkVersion;

        public bool Equals(FrameworkVersion? other) => other is not null && other.RawValue == RawValue;
        public override int GetHashCode() => RawValue.GetHashCode();

        public override string ToString() => $".NET {Version}, built from SHA {CommitHash}";
    }
}