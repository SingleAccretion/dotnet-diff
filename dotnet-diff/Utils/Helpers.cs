using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace DotnetDiff
{
    public static class Helpers
    {
        public static T Resolve<TKey, T>(Dictionary<TKey, T> store, TKey key, bool isAvailable, Func<T> get, Action create) where TKey : notnull
        {
            if (!store.ContainsKey(key))
            {
                if (isAvailable)
                {
                    store.Add(key, get());
                }
                else
                {
                    create();
                    Debug.Assert(store.TryGetValue(key, out var item) && item is not null);
                }
            }

            return store[key];
        }

        public static T Resolve<T>(ref T? item, bool isAvailable, Func<T> get, Action create)
        {
            if (item is null)
            {
                if (isAvailable)
                {
                    item = get();
                }
                else
                {
                    create();
                    Debug.Assert(item is not null);
                }
            }

            return item;
        }

        public static T Resolve<T>(bool isAvailable, Func<T> get, Func<T> create) => isAvailable ? get() : create();

        public static void DownloadPackageFromDotnetFeed(string item, string packageName, string packageVersion, int majorDotnetVersion, IConsole console, Action<string> onDataSaved)
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
    }
}
