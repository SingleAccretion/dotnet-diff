﻿using System;
using System.CommandLine.Invocation;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace DotnetDiff
{
    public static class IO
    {
        private static readonly ThreadLocal<HttpClient> _httpClient = new(() => new HttpClient());

        public static string ExecutableFileName(string baseName) => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

        public static string LibraryFileName(string baseName) =>
            OperatingSystem.IsWindows() ? $"{baseName}.dll" :
            OperatingSystem.IsLinux() ? $"lib{baseName}.so" :
            OperatingSystem.IsMacOS() ? $"lib{baseName}.dylib" :
            throw new NotSupportedException($"Unsupported host OS");

        public static void EnsureFileExists(string file)
        {
            if (!File.Exists(file))
            {
                File.WriteAllBytes(file, Array.Empty<byte>());
            }
        }

        public static void EnsureExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public static void EnsureDeletion(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var childDir in Directory.GetDirectories(dir))
            {
                EnsureDeletion(childDir);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            Directory.Delete(dir);
        }

        public static void Move(string source, string destination)
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(source, destination);
        }

        public static void Invoke(string command, string args, string workingDirectory)
        {
            Process.StartProcess(command, args, workingDirectory).WaitForExit();
        }

        public static HttpStatusCode Download(string downloadUrl, string destinationFilePath, Action<long?, long?> reporter)
        {
            void TriggerProgressChanged(long? totalDownloadSize, long? totalBytesRead) => reporter(totalBytesRead, totalDownloadSize);

            TriggerProgressChanged(null, null);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };

            using var response = RequestGet(downloadUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode;
            }

            using var contentStream = response.Content.ReadAsStream();

            var totalBytes = response.Content.Headers.ContentLength;

            TriggerProgressChanged(totalBytes, 0);

            var totalDownloadSize = totalBytes;
            var totalBytesRead = 0L;
            var savedTotalBytesRead = 0L;
            var buffer = new byte[8192];
            var bytesRead = 0;
            var timesRead = 0;

            using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
            do
            {
                bytesRead = contentStream.Read(buffer);
                fileStream.Write(buffer.AsSpan(0, bytesRead));

                totalBytesRead += bytesRead;

                if (totalDownloadSize is not null)
                {
                    var delta = 100_000 * (totalBytesRead - savedTotalBytesRead) / totalDownloadSize;
                    var percentage = 100_000 * totalBytesRead / totalDownloadSize;
                    if (delta > 1_000 && percentage <= 99_000)
                    {
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                        savedTotalBytesRead = totalBytesRead;
                    }
                }
                else
                {
                    timesRead++;
                    if (timesRead % 50 is 0)
                    {
                        TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    }
                }

                if (bytesRead is 0)
                {
                    break;
                }
            }
            while (true);

            TriggerProgressChanged(totalDownloadSize, totalBytesRead);

            return response.StatusCode;
        }

        public static HttpResponseMessage RequestGet(string uri, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead, Action<HttpRequestMessage>? modifyRequest = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            modifyRequest?.Invoke(request);

            System.Diagnostics.Debug.Assert(_httpClient.Value is not null);
            return _httpClient.Value.Send(request, completionOption);
        }
    }
}
