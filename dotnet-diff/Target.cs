using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DotnetDiff
{
    public class Target
    {
        private readonly string _rootPath;
        private readonly FrameworkVersion _version;
        private readonly RuntimeIdentifier _runtimeIdentifier;
        private readonly ProgramMetadata _metadata;
        private readonly IConsole _console;

        private RuntimeAssemblies? _runtimeAssemblies;
        private Jit? _jit;

        public Target(string rootPath, FrameworkVersion version, RuntimeIdentifier runtimeIdentifier, ProgramMetadata metadata, IConsole console)
        {
            _rootPath = rootPath;
            _version = version;
            _runtimeIdentifier = runtimeIdentifier;
            _metadata = metadata;
            _console = console;
        }

        public RuntimeAssemblies ResolveRuntimeAssemblies() => Helpers.Resolve(
            ref _runtimeAssemblies,
            _metadata.RuntimeAssembliesAreAvailable(_version, _runtimeIdentifier),
            () => RuntimeAssemblies.FromDirectory(RuntimeAssembliesDirectory()),
            () =>
            {
                _console.Out.WriteLine($"Failed to find the runtime assemblies for {_runtimeIdentifier}");
                InstallRuntimeAssemblies();
            });

        public Jit ResolveJit() => Helpers.Resolve(
            ref _jit,
            _metadata.JitIsAvailable(_version, _runtimeIdentifier),
            () => Jit.FromPath(JitPath()),
            () =>
            {
                _console.Out.WriteLine($"Failed to find the Jit for {_runtimeIdentifier}");
                InstallJit();
            });

        public void InstallRuntimeAssemblies()
        {
            var path = RuntimeAssembliesDirectory();

            var packageName = $"Microsoft.NETCore.App.Runtime.{_runtimeIdentifier}";
            var packageVersion = _version.Version;

            _console.Out.WriteLine($"Installing runtime assemblies for {_runtimeIdentifier}");
            Helpers.DownloadPackageFromDotnetFeed("runtime assemblies", packageName, packageVersion, _version.MajorDotnetVersion, _console, pkgDir =>
            {
                IO.Move(Path.Combine(pkgDir, "runtimes", _runtimeIdentifier.ToString(), "lib", _version.Moniker), path);
                _console.Out.WriteLine($"Copied runtime assemblies to '{path}'");

                _runtimeAssemblies = RuntimeAssemblies.FromDirectory(path);
                _metadata.AddRuntimeAssemblies(_version, _runtimeIdentifier);
            });
        }

        public void InstallJit()
        {
            List<string>? GetCommits(string parent, out HttpStatusCode statusCode)
            {
                using var client = new HttpClient();

                var uri = "https://api.github.com/repos/dotnet/runtime/commits?";
                uri += $"sha={parent}&path=src/coreclr/jit";

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new("application/vnd.github.v3+json"));
                request.Headers.UserAgent.Add(new("dotnet-diff", Program.AppVersion));

                _console.WriteLineDebug($"GET {uri}");
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

                return hashes.Count is not 0 ? hashes : null;
            }

            if (!Jit.Exists(_runtimeIdentifier))
            {
                throw new UserException($"There are no builds of the Jit for {_runtimeIdentifier}");
            }

            var path = JitDirectory();
            IO.EnsureExists(path);

            _console.Out.WriteLine($"Installing the Jit for {_runtimeIdentifier}");

            var searchDepth = 10;
            var parentCommit = _version.CommitHash;
            while (true)
            {
                _console.Out.WriteLine($"Trying to find a checked {Jit.GetJitName(_runtimeIdentifier)} built before {parentCommit}");

                List<string>? commits = GetCommits(parentCommit, out var code);
                if (commits is null)
                {
                    throw new UserException($"Failed to retrieve the commits, server returned: {code}");
                }

                _console.Out.Write("Searching in blob storage for available builds");
                foreach (var commit in commits)
                {
                    var jitName = Jit.GetJitName(_runtimeIdentifier);
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

                    _console.WriteLineDebug($"GET {uri}");
                    bool jitFound = false;

                    var status = IO.Download(uri, tmpFile, (read, total) =>
                    {
                        if (read is not null && total is not null)
                        {
                            if (!jitFound)
                            {
                                jitFound = true;
                                _console.Out.WriteLine();
                                _console.Out.WriteLine($"GET {uri}");
                            }

                            TerminalProgressReporter.Report(read, total, _console);
                        }
                        else
                        {
                            _console.Out.Write(".");
                        }
                    });

                    if (status != HttpStatusCode.OK)
                    {
                        _console.WriteLineDebug($"Jit not found for commit: '{commit}', server returned {status}");

                        parentCommit = commits[^1];
                        continue;
                    }

                    _console.Out.WriteLine($"Downloaded {jitName} built from {commit}");
                    var jitPath = Path.Combine(path, jitName);
                    File.Move(tmpFile, jitPath, overwrite: true);
                    _console.Out.WriteLine($"Copied the Jit to '{jitPath}'");

                    _jit = Jit.FromPath(jitPath);
                    _metadata.AddJit(_version, _runtimeIdentifier);
                    return;
                }

                if (searchDepth is 0)
                {
                    _console.Out.WriteLine($"Failed to find any Jit in the rolling builds storage for commit range {commits[^1]}-{_version.CommitHash}");
                }

                _console.Out.WriteLine();
                searchDepth--;
            }
        }

        private string RuntimeAssembliesDirectory() => Path.Combine(_rootPath, "RuntimeAssemblies");
        private string JitDirectory() => Path.Combine(_rootPath, "Jit");
        private string JitPath() => Path.Combine(JitDirectory(), Jit.GetJitName(_runtimeIdentifier));
    }
}
