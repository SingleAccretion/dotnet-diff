using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DotnetDiff
{
    public class Sdk
    {
        public static string PathToSDKInstalls { get; } = Path.Combine(Program.AppDir, "SDKs");

        private const int LatestDotnetVersion = 6;

        public static FrameworkVersion SupportedSdkVersion { get; } = new("6.0.0-preview.4.21205.3+7b9ab0e196c78968bac455bf29a9845a85e4a022");

        private readonly Dictionary<RuntimeIdentifier, Target> _targets;
        
        private Sdk(Crossgen2 crossgen2, IReadOnlyDictionary<RuntimeIdentifier, Target> targets)
        {
            Crossgen2 = crossgen2;

            _targets = targets.ToDictionary(x => x.Key, x => x.Value);
        }

        public Crossgen2 Crossgen2 { get; }

        public static Sdk Resolve(FrameworkVersion version, RuntimeIdentifier[] targetRuntimeIdentifiers, ProgramMetadata metadata, IConsole console)
        {
            var builder = new SdkBuilder(version);

            if (!metadata.FullSdkIsAvailable(version))
            {
                console.Out.WriteLine($"Failed to find the SDK for {version}");
                InstallSdk(builder, console);
                metadata.AddFullSdk(version);
            }

            foreach (var targetRuntimeIdentifier in targetRuntimeIdentifiers)
            {
                if (!metadata.FullTargetIsAvailable(version, targetRuntimeIdentifier))
                {
                    console.Out.WriteLine($"Failed to find the SDK bits needed to target {targetRuntimeIdentifier}");
                    InstallTarget(builder, targetRuntimeIdentifier, console);
                    metadata.AddFullTarget(version, targetRuntimeIdentifier);
                }
            }

            console.Out.WriteLine($"Using the SDK at: '{SdkDirectory(version)}'");

            return builder.Build();
        }

        public static Sdk Install(FrameworkVersion version, RuntimeIdentifier[] targetRuntimeIdentifiers, ProgramMetadata metadata, IConsole console)
        {
            var builder = new SdkBuilder(version);

            InstallSdk(builder, console);
            metadata.AddFullSdk(version);

            foreach (var runtimeIdentifier in targetRuntimeIdentifiers)
            {
                InstallTarget(builder, runtimeIdentifier, console);
                metadata.AddFullTarget(version, runtimeIdentifier);
            }

            return builder.Build();
        }

        public static Jit InstallJit(FrameworkVersion version, RuntimeIdentifier targetRuntimeIdentifier, ProgramMetadata metadata, IConsole console)
        {
            var dir = JitDirectory(version, targetRuntimeIdentifier);
            var builder = new TargetBuilder(version, targetRuntimeIdentifier, dir);
            InstallJit(builder, console);

            metadata.AddJit(version, targetRuntimeIdentifier);

            return Jit.CreateFromPath(Path.Combine(dir, Jit.GetJitName(targetRuntimeIdentifier)));
        }

        public Jit JitForRid(RuntimeIdentifier runtimeIdentifier) => _targets.TryGetValue(runtimeIdentifier, out var target) ? target.Jit : throw new Exception($"No Jit could be found for {runtimeIdentifier}");

        private static void InstallSdk(SdkBuilder builder, IConsole console)
        {
            var version = builder.Version;

            if (version != SupportedSdkVersion)
            {
                throw new NotSupportedException($"Installing SDK for {version} is not supported");
            }

            console.Out.WriteLine($"Installing the SDK for {version}");

            var path = SdkDirectory(version);
            IO.EnsureDeletion(path);
            IO.EnsureExists(path);

            InstallCrossgen2(builder, console);
        }

        private static void InstallTarget(SdkBuilder builder, RuntimeIdentifier runtimeIdentifier, IConsole console)
        {
            var path = TargetDirectory(builder.Version, runtimeIdentifier);
            var targetBuilder = new TargetBuilder(builder.Version, runtimeIdentifier, path);

            IO.EnsureDeletion(path);
            IO.EnsureExists(path);

            console.Out.WriteLine($"Installing the SDK bits needed to target {runtimeIdentifier}");
            InstallRuntimeAssemblies(targetBuilder, console);
            InstallJit(targetBuilder, console);

            builder.AddTarget(runtimeIdentifier, targetBuilder.Build());
        }

        private static void InstallRuntimeAssemblies(TargetBuilder builder, IConsole console)
        {
            var path = RuntimeAssembliesDirectory(builder.Version, builder.Target);
            IO.EnsureDeletion(path);

            var packageName = $"Microsoft.NETCore.App.Runtime.{builder.Target}";
            var packageVersion = builder.Version.Version;

            DownloadPackageFromDotnetFeed("runtime assemblies", packageName, packageVersion, LatestDotnetVersion, console, pkgDir =>
            {
                Directory.Move(Path.Combine(pkgDir, "runtimes", builder.Target.ToString(), "lib", builder.Version.Moniker), path);
                console.Out.WriteLine($"Copied runtime assemblies to '{path}'");

                builder.AddRuntimeAssemblies(RuntimeAssemblies.FromDirectory(path));
            });
        }

        private static void InstallJit(TargetBuilder builder, IConsole console)
        {
            List<string>? GetCommits(string parent, out HttpStatusCode statusCode)
            {
                using var client = new HttpClient();

                var uri = "https://api.github.com/repos/dotnet/runtime/commits?";
                uri += $"sha={parent}&path=src/coreclr/jit";

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new("application/vnd.github.v3+json"));
                request.Headers.UserAgent.Add(new("dotnet-diff", Program.AppVersion));

                console.WriteLineDebug($"GET {uri}");
                var response = client.Send(request);

                statusCode = response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var hashes = new List<string>();
                var commits = JsonDocument.Parse(response.Content.ReadAsStream()).RootElement;
                foreach (var commit in commits.EnumerateArray())
                {
                    hashes.Add(commit.GetProperty("sha").GetString() ?? throw new Exception($"Unexpected JSON in parsing the commit hashes for the Jit"));
                }

                return hashes.Any() ? hashes : null;
            }

            var path = JitDirectory(builder.Version, builder.Target);

            IO.EnsureDeletion(path);
            IO.EnsureExists(path);

            var searchDepth = 10;
            var parentCommit = builder.Version.CommitHash;
            while (true)
            {
                console.Out.WriteLine($"Trying to find a checked {Jit.GetJitName(builder.Target)} built before {parentCommit}");

                List<string>? commits = GetCommits(parentCommit, out var code);
                if (commits is null)
                {
                    throw new Exception($"Failed to retrieve the commits, server returned: {code}");
                }

                console.Out.Write("Searching in blob storage for available builds");
                foreach (var commit in commits)
                {
                    var jitName = Jit.GetJitName(builder.Target);
                    var arch = RuntimeIdentifier.Host.Architecture.ToString().ToLowerInvariant();
                    var os = RuntimeIdentifier.Host.Platform switch
                    {
                        Platform.Windows => "windows",
                        Platform.Linux => "Linux",
                        Platform.MacOS => "OSX",
                        _ => throw new NotSupportedException($"Unsupported OS: {RuntimeIdentifier.Host.Platform}")
                    };

                    var uri = $"https://clrjit2.blob.core.windows.net/jitrollingbuild/builds/{commit}/{os}/{arch}/Checked/{jitName}";

                    var tmpFile = Path.GetTempFileName();

                    console.WriteLineDebug($"GET {uri}");
                    bool jitFound = false;

                    var status = IO.Download(uri, tmpFile, (read, total) =>
                    {
                        if (read is not null && total is not null)
                        {
                            if (!jitFound)
                            {
                                jitFound = true;
                                console.Out.WriteLine();
                                console.Out.WriteLine($"GET {uri}");
                            }

                            TerminalProgressReporter.Report(read, total, console);
                        }
                        else
                        {
                            console.Out.Write(".");
                        }
                    });

                    if (status != HttpStatusCode.OK)
                    {
                        console.WriteLineDebug($"Jit not found for commit: '{commit}', server returned {status}");
                        
                        parentCommit = commits[^1];
                        continue;
                    }
                    
                    console.Out.WriteLine($"Downloaded {jitName} built from {commit}");
                    var jitPath = Path.Combine(path, jitName);
                    File.Move(tmpFile, jitPath, overwrite: true);
                    console.Out.WriteLine($"Copied the Jit to '{jitPath}'");

                    builder.AddJit(Jit.CreateFromPath(jitPath));
                    return;
                }

                if (searchDepth is 0)
                {
                    console.Out.WriteLine($"Failed to find any Jit in the rolling builds storage for commit range {commits[^1]}-{builder.Version.CommitHash}");
                }

                console.Out.WriteLine();
                searchDepth--;
            }
        }

        private static void InstallCrossgen2(SdkBuilder builder, IConsole console)
        {
            var path = Crossgen2Directory(builder.Version);
            IO.EnsureDeletion(path);

            var packageName = $"Microsoft.NETCore.App.Crossgen2.{RuntimeIdentifier.Host}";
            var packageVersion = builder.Version.Version;

            DownloadPackageFromDotnetFeed("crossgen2", packageName, packageVersion, LatestDotnetVersion, console, pkgDir =>
            {
                Directory.Move(Path.Combine(pkgDir, "tools"), path);
                console.Out.WriteLine($"Copied crossgen2 to '{path}'");
            });

            builder.AddCrossgen2(Crossgen2.CreateFromPath(Path.Combine(path, IO.ExecutableFileName("crossgen2"))));
        }

        private static void DownloadPackageFromDotnetFeed(string item, string packageName, string packageVersion, int majorDotnetVersion, IConsole console, Action<string> onDataSaved)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"DotnetDiff-{item}-{Guid.NewGuid()}");
            try
            {
                IO.EnsureExists(tempDir);
                var tempFile = Path.Combine(tempDir, "package");
                IO.EnsureFileExists(tempFile);

                var url = @"https://pkgs.dev.azure.com/dnceng/public/_apis/packaging/feeds/";
                url += $"dotnet{majorDotnetVersion}/";
                url += "nuget/packages/";
                url += $"{packageName}/";
                url += "versions/";
                url += $"{packageVersion}/";
                url += "content?api-version=6.0-preview.1";

                console.Out.WriteLine($"GET {url}");
                var response = IO.Download(url, tempFile, (read, total) => TerminalProgressReporter.Report(read, total, console));
                if (response != HttpStatusCode.OK)
                {
                    throw new Exception($"Failed to download {item}, server returned: '{response}'");
                }
                console.Out.WriteLine($"Downloaded {packageName}, version {packageVersion}");

                var extractDir = Path.Combine(tempDir, "extract");
                ZipFile.ExtractToDirectory(tempFile, extractDir);
                console.Out.WriteLine($"Extracted {item}");

                onDataSaved(extractDir);
            }
            finally
            {
                if (Config.DeleteTempFiles)
                {
                    IO.EnsureDeletion(tempDir);
                }
            }
        }

        private static string SdkDirectory(FrameworkVersion version) => Path.Combine(PathToSDKInstalls, version.RawValue);
        private static string Crossgen2Directory(FrameworkVersion version) => Path.Combine(SdkDirectory(version), "Crossgen2");
        private static string TargetDirectory(FrameworkVersion version, RuntimeIdentifier runtimeIdentifier) => Path.Combine(SdkDirectory(version), runtimeIdentifier.ToString());
        private static string RuntimeAssembliesDirectory(FrameworkVersion version, RuntimeIdentifier runtimeIdentifier) => Path.Combine(TargetDirectory(version, runtimeIdentifier), "RuntimeAssemblies");
        private static string JitDirectory(FrameworkVersion version, RuntimeIdentifier runtimeIdentifier) => Path.Combine(TargetDirectory(version, runtimeIdentifier), "Jit");
        private static string JitPath(FrameworkVersion version, RuntimeIdentifier runtimeIdentifier) => Path.Combine(JitDirectory(version, runtimeIdentifier), Jit.GetJitName(runtimeIdentifier));

        private class SdkBuilder
        {
            private readonly Dictionary<RuntimeIdentifier, Target> _targets = new Dictionary<RuntimeIdentifier, Target>();
            private Crossgen2? _crossgen2;

            public SdkBuilder(FrameworkVersion version)
            {
                Version = version;
            }

            public FrameworkVersion Version { get; }
            public void AddCrossgen2(Crossgen2 crossgen2) => _crossgen2 = crossgen2;

            public void AddTarget(RuntimeIdentifier runtimeIdentifier, Target target) => _targets.Add(runtimeIdentifier, target);

            public Sdk Build()
            {
                Debug.Assert(_crossgen2 is not null);

                return new(_crossgen2, _targets);
            }
        }

        private class TargetBuilder
        {
            private RuntimeAssemblies? _runtimeAssemblies;
            private readonly string _path;
            private Jit? _jit;

            public TargetBuilder(FrameworkVersion version, RuntimeIdentifier target, string path)
            {
                Version = version;
                Target = target;
                _path = path;
            }

            public FrameworkVersion Version { get; }
            public RuntimeIdentifier Target { get; }

            public void AddRuntimeAssemblies(RuntimeAssemblies runtimeAssemblies) => _runtimeAssemblies = runtimeAssemblies;

            public void AddJit(Jit jit) => _jit = jit;

            public Target Build()
            {
                Debug.Assert(_runtimeAssemblies is not null);
                Debug.Assert(_jit is not null);

                return new(_runtimeAssemblies, _jit);
            }
        }
    }
}