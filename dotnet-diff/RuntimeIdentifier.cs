using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace DotnetDiff
{
    public record RuntimeIdentifier
    {
        public RuntimeIdentifier(Platform platform, Architecture architecture)
        {
            Platform = platform;
            Architecture = architecture;
        }
        
        public static RuntimeIdentifier Host { get; } = GetCurrentMachineIdentifier();

        public Platform Platform { get; }
        public Architecture Architecture { get; }

        public static RuntimeIdentifier Parse(string input)
        {
            var parts = input.Split('-');
            if (parts.Length < 2)
            {
                throw new FormatException("Runtime identifier must consist of at least two parts");
            }

            var platform = parts[0].ToLowerInvariant() switch
            {
                "win" => Platform.Windows,
                "linux" => Platform.Linux,
                "osx" => Platform.MacOS,
                _ => throw new Exception($"Unsupported OS: '{parts[0]}'")
            };

            if (!Enum.TryParse<Architecture>(parts[1], ignoreCase: true, out var arch))
            {
                throw new Exception($"Unsupported CPU architecture: '{parts[0]}'");
            }

            return new(platform, arch);
        }

        public override string ToString()
        {
            static string ToRidFormat(Platform platform) => platform switch
            {
                Platform.Windows => "win",
                Platform.Linux => "linux",
                Platform.MacOS => "osx",
                _ => throw new Exception($"Unsupported OS: {platform}")
            };

            return $"{ToRidFormat(Platform)}-{Architecture}".ToLowerInvariant();
        }

        private static RuntimeIdentifier GetCurrentMachineIdentifier()
        {
            var os = OperatingSystem.IsWindows() ? Platform.Windows :
                     OperatingSystem.IsLinux() ? Platform.Linux :
                     OperatingSystem.IsMacOS() ? Platform.MacOS :
                     throw new Exception($"Unsupported platform");

            var arch = (ArmBase.IsSupported, X86Base.IsSupported, Environment.Is64BitOperatingSystem) switch
            {
                (_, true, false) => Architecture.X86,
                (_, true, true) => Architecture.X64,
                (true, _, false) => Architecture.Arm,
                (true, _, true) => Architecture.Arm64,
                _ => throw new Exception($"Unsupported CPU architecture")
            };

            return new(os, arch);
        }
    }
}
