using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;

namespace DotnetDiff
{
    public class Sdk
    {
        public static string PathToSDKInstalls { get; } = Path.Combine(Program.AppDir, "SDKs");

        private const int LatestDotnetVersion = 6;

        public static FrameworkVersion SupportedSdkVersion { get; } = new("6.0.0-preview.4.21205.3+7b9ab0e196c78968bac455bf29a9845a85e4a022");

        private readonly string _rootPath;
        private readonly FrameworkVersion _version;
        private readonly ProgramMetadata _metadata;
        private readonly IConsole _console;
        private readonly Dictionary<RuntimeIdentifier, Target> _targets = new();

        private Crossgen2? _crossgen2;

        private Sdk(string rootPath, FrameworkVersion version, ProgramMetadata metadata, IConsole console)
        {
            _rootPath = rootPath;
            _version = version;
            _metadata = metadata;
            _console = console;
        }

        public static Sdk Resolve(FrameworkVersion version, ProgramMetadata metadata, IConsole console) => Helpers.Resolve(
            metadata.SdkIsAvailable(version),
            () => new(SdkDirectory(version), version, metadata, console), () =>
            {
                console.Out.WriteLine($"Failed to find the SDK for {version}");
                return DefineSdk(version, metadata, console);
            });

        public static Sdk Install(FrameworkVersion version, RuntimeIdentifier[] targetRuntimeIdentifiers, ProgramMetadata metadata, IConsole console)
        {
            console.Out.WriteLine($"Installing the SDK for {version}");

            var sdk = DefineSdk(version, metadata, console);
            sdk.InstallCrossgen2();

            foreach (var runtimeIdentifier in targetRuntimeIdentifiers)
            {
                sdk.InstallTarget(runtimeIdentifier);
            }

            return sdk;
        }

        public Crossgen2 ResolveCrossgen2() => Helpers.Resolve(
            ref _crossgen2,
            _metadata.Crossgen2IsAvailable(_version),
            () => Crossgen2.FromPath(Crossgen2Path()),
            () =>
            {
                _console.Out.WriteLine($"Failed to find Crossgen2 for {_version}");
                InstallCrossgen2();
            });

        public void InstallCrossgen2()
        {
            var path = Crossgen2Directory();

            var packageName = $"Microsoft.NETCore.App.Crossgen2.{RuntimeIdentifier.Host}";
            var packageVersion = _version.Version;

            _console.Out.WriteLine($"Installing Crossgen2 for {_version.Version}");
            Helpers.DownloadPackageFromDotnetFeed("crossgen2", packageName, packageVersion, LatestDotnetVersion, _console, pkgDir =>
            {
                IO.Move(Path.Combine(pkgDir, "tools"), path);
                _console.Out.WriteLine($"Copied crossgen2 to '{path}'");

                _crossgen2 = Crossgen2.FromPath(Path.Combine(path, IO.ExecutableFileName("crossgen2")));
                _metadata.AddCrossgen2(_version);
            });
        }

        public Target ResolveTarget(RuntimeIdentifier runtimeIdentifier) => Helpers.Resolve(
            _targets, runtimeIdentifier,
            _metadata.TargetIsAvailable(_version, runtimeIdentifier),
            () => new(TargetDirectory(runtimeIdentifier), _version, runtimeIdentifier, _metadata, _console),
            () =>
            {
                _console.Out.WriteLine($"Failed to find the {runtimeIdentifier} target");
                DefineTarget(runtimeIdentifier);
            });

        private static Sdk DefineSdk(FrameworkVersion version, ProgramMetadata metadata, IConsole console)
        {
            if (version != SupportedSdkVersion)
            {
                throw new NotSupportedException($"Installing SDK for {version} is not supported");
            }

            var path = Path.Combine(PathToSDKInstalls, version.RawValue);
            IO.EnsureExists(path);

            metadata.AddSdk(version);

            return new(path, version, metadata, console);
        }

        private static string SdkDirectory(FrameworkVersion version) => Path.Combine(PathToSDKInstalls, version.RawValue);

        private void InstallTarget(RuntimeIdentifier runtimeIdentifier)
        {
            _console.Out.WriteLine($"Installing {runtimeIdentifier} target");
            DefineTarget(runtimeIdentifier);

            var target = _targets[runtimeIdentifier];
            target.InstallRuntimeAssemblies();
            target.InstallJit();
        }

        private void DefineTarget(RuntimeIdentifier runtimeIdentifier)
        {
            var path = TargetDirectory(runtimeIdentifier);
            IO.EnsureExists(path);

            _targets[runtimeIdentifier] = new Target(path, _version, runtimeIdentifier, _metadata, _console);

            _metadata.AddTarget(_version, runtimeIdentifier);
        }

        private string SdkDirectory() => _rootPath;
        private string Crossgen2Directory() => Path.Combine(SdkDirectory(), "Crossgen2");
        private string Crossgen2Path() => Path.Combine(Crossgen2Directory(), IO.ExecutableFileName("crossgen2"));
        private string TargetDirectory(RuntimeIdentifier runtimeIdentifier) => Path.Combine(SdkDirectory(), runtimeIdentifier.ToString());
    }
}