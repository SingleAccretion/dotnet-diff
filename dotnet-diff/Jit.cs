﻿using System;
using System.Runtime.InteropServices;

namespace DotnetDiff
{
    public sealed class Jit
    {
        private Jit(string path) => Path = path;

        public string Path { get; }

        public static Jit FromPath(string path)
        {
            if (!System.IO.Path.GetFileName(path).Contains("clrjit"))
            {
                throw new Exception($"'{path}' is not a path to a Jit compiler");
            }

            return new(path);
        }

        public static string GetJitName(RuntimeIdentifier target)
        {
            var baseName = "clrjit";

            if (RuntimeIdentifier.Host == target)
            {
                return IO.LibraryFileName(baseName);
            }

            var os = target.Platform switch
            {
                Platform.Windows => "win",
                Platform.Linux => "unix",
                Platform.MacOS when target.Architecture is Architecture.X64 => "unix",
                Platform.MacOS when target.Architecture is Architecture.Arm64 => "unix_osx",
                _ => throw new NotSupportedException($"Unsupported OS: {target.Platform}")
            };
            var arch = target.Architecture.ToString().ToLowerInvariant();
            var hostArch = RuntimeIdentifier.Host.Architecture.ToString().ToLowerInvariant();

            return IO.LibraryFileName($"{baseName}_{os}_{arch}_{hostArch}");
        }

        public static bool Exists(RuntimeIdentifier target) => target.ToString() is
            "win-x64" or
            "win-x86" or
            "win-arm" or
            "win-arm64" or
            "linux-x64" or
            "linux-arm" or
            "linux-arm64" or
            "osx-x64" or
            "osx-arm64";
    }
}