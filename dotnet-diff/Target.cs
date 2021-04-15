using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
            Helpers.DownloadPackageFromDotnetFeed("runtime assemblies", packageName, packageVersion, _version.Major, _console, pkgDir =>
            {
                IO.Move(Path.Combine(pkgDir, "runtimes", _runtimeIdentifier.ToString(), "lib", _version.Moniker), path);
                _console.Out.WriteLine($"Copied runtime assemblies to '{path}'");

                _runtimeAssemblies = RuntimeAssemblies.FromDirectory(path);
                _metadata.AddRuntimeAssemblies(_version, _runtimeIdentifier);
            });
        }

        public void InstallJit()
        {
            // The algorithm for finding the Jit.
            // 1. We can only download the Jit with the Jit-EE interface (I-GUID)
            // matching that of the other tools in the SDK (at present, Crossgen2).
            // 2. We want a build that is is as close as possible to the commit that the SDK
            // itself was built from. This is not for any fundamental reason - it just seems
            // that the expected default behavior for "dotnet-diff install jit -f <version>"
            // would be to install the Jit matching <version> exactly. However, that is not
            // possible as the rolling build does not have binaries for all commits.
            // Thus we search for the closest available.
            // 3. We also want to perform as few GitHub queries as feasible, as
            // there is a limit of 60 requests per hour for unauthenticated users.
            //
            // Thus, we proceed as follows:
            // [GET] 30 (default) commits under the SDK's commit that changed jiteeversionguid.h.
            // Determine from them the lower bound commit - it will be the first in the list.
            // Note: the bound returned is inclusive - it is safe to download the Jit built from it.
            //
            // [GET] 30 commits under the SDK's commit that changes something in src/coreclr/jit.
            // We walk this list (in time - backwards), trying to dowload the build, until:
            // 1. We hit the commit afer the lower bound: try the upper range.
            // 2. We have tried N commits (N - closeness factor, represents a tradeoff between the number of
            // API calls we're willing to make against the "closeness" of the Jit to the SDK).
            // N will be less than 30 - the number of the commits returned from the API call.
            // 
            // Falling back to the upper range, we perform the same sequence in reversed, only now
            // on commits that came after the SDK's (using the 'since' API option).
            // Failing that, we drop the limit of N and search the commit ranges in a round-robin fashion:
            // lower -> upper -> lower - 1 -> upper + 1 -> ..., performing
            // as many API requests for commits as needed, while we are still in range (using commit dates)
            // for the I-GUID. If no builds have been found that way, we fail - the only option
            // the user has at that point is to clone dotnet/runtime and build the relevant commit themselves.

            JsonElement GetJsonFromGitHub(string endpoint, out HttpStatusCode code)
            {
                var uri = $"https://api.github.com{endpoint}";
                _console.WriteLineDebug($"GET {uri}");

                var response = IO.RequestGet(uri, modifyRequest: request =>
                {
                    request.Headers.Accept.Add(new("application/vnd.github.v3+json"));
                    request.Headers.UserAgent.Add(new("dotnet-diff", Program.AppVersion));
                });

                code = response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    return default;
                }

                return JsonDocument.Parse(response.Content.ReadAsStream()).RootElement;
            }

            string GetBranchName(FrameworkVersion version)
            {
                // We cannot easily deduce the branch name from the commit hash - no GitHub API for that :(.
                // And we need to know it for getting the commits "above" ours.
                // So we'll use some common sense to derive it from the version.

                // It has changed in the past.
                // But let's assume it won't change for at least another 10 years.
                const string MainBranch = "main";

                // I did not want to fuzzy search the available branches here or something like that, I am too weak for that.
                // So, hardcoded branch names they will be and let's pray it doesn't break.
                // Futile this hope is, but what can one do (not much if one is lazy).
                var releases = DotnetReleases.Resolve(_console);
                
                if (version.IsPreview && version.Release == releases.NextRelease?.ShortVersion)
                {
                    var previewNumber = version.GetPreviewNumber();
                    if (previewNumber > releases.LatestPreviewVersionNumber)
                    {
                        // We have the latest bits.
                        // Note that this handles the case of "null" "releasedPreviewNumber", which
                        // has the meaning of no previews having been released so far for the next release.
                        return MainBranch;
                    }
                    else
                    {
                        // Let's grab our preview branch then, and hope the format won't change.
                        return $"release/{version.Release}-preview.{previewNumber}";
                    }
                }

                // At his point, we are either looking at an LTS release or a "current" release or
                // someone is requesting a preview for a released version.
                // We don't handle 3.1 and 2.1 here because they are blocked by FrameworkVersion.
                // As for the preview case - well, let's hope its commits have been merged into
                // the release branch. That won't be the case for some servicing commits, but we can't do much better.
                // Note: we don't care for invalid versions here, like someone requesting .NET 9000.
                // Because if someone is doing that, well, that's on them.
                return $"release/{_version.Release}";
            }

            List<(string Hash, DateTimeOffset Date)>? GetCommits(string baseCommit, string path, out HttpStatusCode code, DateTimeOffset? since = null)
            {
                // The Git tree only stores parents, the GitHub API reflects that - so we need to figure out a parent for our
                // commit when using the "since" option. In practice, this means that we need to figure out the branch to which
                // it belongs.
                var baseTree = since is null ? baseCommit : GetBranchName(_version);

                using var client = new HttpClient();

                var uri = "/repos/dotnet/runtime/commits?";
                uri += $"sha={baseTree}&path={path}";
                if (since is not null)
                {
                    uri += $"&since={since.Value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture)}";
                }

                var commits = GetJsonFromGitHub(uri, out code);
                if (commits.ValueKind is JsonValueKind.Undefined)
                {
                    return null;
                }

                var hashes = new List<(string, DateTimeOffset)>();

                // Note: the API will return an empty array if there are no commits found,
                // which is really nice - we don't need to special-case anything here.
                foreach (var commit in commits.EnumerateArray())
                {
                    var sha = commit.GetProperty("sha").GetString();
                    var dateString = commit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
                    if (sha is null || dateString is null)
                    {
                        throw new UserException($"Unexpected JSON in parsing the commit hashes for the Jit: {commit}");
                    }

                    var date = DateTimeOffset.Parse(dateString, CultureInfo.InvariantCulture);
                    hashes.Add((sha, date));
                }

                return hashes;
            }

            (string Hash, DateTimeOffset Date) GetBoundForGuidChanges(DateTimeOffset? since = null)
            {
                void ThrowIfNotFound([NotNull] object? commits, HttpStatusCode code)
                {
                    if (commits is null)
                    {
                        throw new UserException($"Failed to retreive the commits for the Jit-EE GUID changes, server returned: {code}");
                    }
                }

                var commits = GetCommits(_version.CommitHash, "src/coreclr/inc/jiteeversionguid.h", out var code, since);
                ThrowIfNotFound(commits, code);

                if (commits.Count is 0)
                {
                    // We may be looking at a request for an older branch.
                    // Try the old file path.
                    var oldBranchCommits = GetCommits(_version.CommitHash, "src/coreclr/src/inc/jiteeversionguid.h", out code);
                    ThrowIfNotFound(oldBranchCommits, code);

                    if (oldBranchCommits.Count is 0)
                    {
                        // We may be looking at a situation where there are no commits above use that changed the I-GUID.
                        // Any commit is fair game then.
                        if (since is not null)
                        {
                            return ("<No commit>", DateTimeOffset.MaxValue);
                        }

                        throw new UserException($"Could not retrive the commits changing the Jit-EE interface");
                    }
                }

                return since is null ? commits[0] : commits[^1];
            }

            List<(string Hash, DateTimeOffset Date)> GetJitCommits(DateTimeOffset? since = null)
            {
                void ThrowIfNotFound([NotNull] object? commits, HttpStatusCode code)
                {
                    if (commits is null)
                    {
                        throw new UserException($"Commits could not be retirved for the changes in the Jit, server returned {code}");
                    }
                }

                // We search for commits that touched the Jit to maximize our chances
                // of getting the build from the rolling build storage.
                var commits = GetCommits(_version.CommitHash, "src/coreclr/jit", out var code, since);
                ThrowIfNotFound(commits, code);

                // Try the old path - we may be looking for some old commits.
                if (commits.Count is 0)
                {
                    commits = GetCommits(_version.CommitHash, "src/coreclr/src/jit", out code, since);
                    ThrowIfNotFound(commits, code);

                    // We can legitimately have no commits above us.
                    if (since is not null && commits.Count is 0)
                    {
                        return commits;
                    }

                    throw new UserException($"Was not able to retrive commits for changes that affected the Jit, server returned {code}");
                }

                // Reverse the order of commits to make the logic using this method simpler
                // in cases when we want to look for commits above ours.
                if (since is not null)
                {
                    commits.Reverse();
                }

                return commits;
            }

            bool TryDownloadJitFromRollingBuild(string commit, string jitDirectory)
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
                            if (!Config.DebugLog)
                            {
                                _console.Out.WriteLine();
                            }
                            _console.Out.WriteLine($"GET {uri}");
                        }

                        TerminalProgressReporter.Report(read, total, _console);
                    }
                    else
                    {
                        if (!Config.DebugLog)
                        {
                            _console.Out.Write(".");
                        }
                    }
                });

                if (status != HttpStatusCode.OK)
                {
                    _console.WriteLineDebug($"Jit not found for commit: '{commit}', server returned {status}");
                    return false;
                }

                _console.Out.WriteLine($"Downloaded {jitName} built from {commit}");
                var jitPath = Path.Combine(jitDirectory, jitName);
                File.Move(tmpFile, jitPath, overwrite: true);
                _console.Out.WriteLine($"Copied the Jit to '{jitPath}'");

                _jit = Jit.FromPath(jitPath);
                _metadata.AddJit(_version, _runtimeIdentifier);

                return true;
            }

            if (!Jit.Exists(_runtimeIdentifier))
            {
                throw new UserException($"There are no builds of the Jit for {_runtimeIdentifier}");
            }

            var path = JitDirectory();
            IO.EnsureExists(path);

            _console.Out.WriteLine($"Installing the Jit for {_runtimeIdentifier}");

            const int N = 10;

            _console.Out.Write($"Looking for a checked {Jit.GetJitName(_runtimeIdentifier)} built close to {_version.CommitHash}");
            _console.WriteLineDebug();

            var commits = GetJitCommits();
            if (!Config.PreferLaterCommitsForJitInstallation)
            {
                // Try the commits under us.
                var (lwoerBoundHash, lowerBoundDate) = GetBoundForGuidChanges();
                _console.WriteLineDebug($"Trying {N} commits below the SDK's, the lower bound date is {lwoerBoundHash}");
                for (int i = 0; i < N; i++)
                {
                    var (commit, date) = commits[i];
                    if (!(date >= lowerBoundDate))
                    {
                        break;
                    }
                    if (TryDownloadJitFromRollingBuild(commit, path))
                    {
                        return;
                    }
                }
            }

            // Try the commits above us.
            Debug.Assert(_version.CommitHash == commits[0].Hash);
            var thisCommitDate = commits[0].Date;
            var (upperBoundHash, upperBoundDate) = GetBoundForGuidChanges(thisCommitDate);
            commits = GetJitCommits(thisCommitDate);
            _console.WriteLineDebug($"Trying {N} commits above the SDK's, the upper bound's date is {upperBoundHash}");
            for (int i = 0; i < N; i++)
            {
                var (commit, date) = commits[i];
                if (!(date <= upperBoundDate))
                {
                    break;
                }
                if (TryDownloadJitFromRollingBuild(commit, path))
                {
                    return;
                }
            }

            throw new NotImplementedException("Implement the round-robin fallback");
        }

        private string RuntimeAssembliesDirectory() => Path.Combine(_rootPath, "RuntimeAssemblies");
        private string JitDirectory() => Path.Combine(_rootPath, "Jit");
        private string JitPath() => Path.Combine(JitDirectory(), Jit.GetJitName(_runtimeIdentifier));
    }
}
