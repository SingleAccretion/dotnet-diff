﻿using System;
using System.CommandLine;
using System.CommandLine.IO;

namespace DotnetDiff
{
    public static class Config
    {
        public static bool DeleteTempFiles { get; } = Environment.GetEnvironmentVariable("DOTNET_DIFF_DELETE_TEMP_FILES") is not "1";
        public static bool DebugLog { get; } = Environment.GetEnvironmentVariable("DOTNET_DIFF_DEBUG_LOG") is "1";
        public static bool PreferLaterCommitsForJitInstallation { get; } = Environment.GetEnvironmentVariable("DOTNET_DIFF_PREFER_LATER_COMMITS_FOR_JIT_INSTALLATION") is "1";

        public static void WriteLineDebug(this IConsole console, string message)
        {
            if (DebugLog)
            {
                console.Out.WriteLine(message);
            }
        }

        public static void WriteLineDebug(this IConsole console) => console.WriteLineDebug("");
    }
}
