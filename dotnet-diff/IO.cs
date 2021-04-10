﻿using System;
using System.CommandLine.Invocation;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DotnetDiff
{
    public static class IO
    {
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

        public static void EnsureExists(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
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

        public static void Invoke(string command, string args, string workingDirectory)
        {
            Process.StartProcess(command, args, workingDirectory).WaitForExit();
        }

        public static HttpStatusCode Download(string downloadUrl, string destinationFilePath, Action<long?, long?> reporter)
        {
            void TriggerProgressChanged(long? totalDownloadSize, long? totalBytesRead) => reporter(totalBytesRead, totalDownloadSize);

            TriggerProgressChanged(null, null);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };

            var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            using var response = httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode;
            }

            using var contentStream = response.Content.ReadAsStream();
            
            var totalBytes = response.Content.Headers.ContentLength;

            TriggerProgressChanged(totalBytes, 0);

            var totalDownloadSize = totalBytes;
            var totalBytesRead = 0L;
            var readCount = 0L;
            var buffer = new byte[8192];
            var isMoreToRead = true;

            using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
            do
            {
                var bytesRead = contentStream.Read(buffer);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                fileStream.Write(buffer.AsSpan(0, bytesRead));

                totalBytesRead += bytesRead;
                readCount += 1;

                if (readCount % 100 == 0)
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
            }
            while (isMoreToRead);

            return response.StatusCode;
        }
    }
}