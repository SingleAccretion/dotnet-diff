using System;

namespace DotnetDiff
{
    public sealed class Jit
    {
        private Jit(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static Jit CreateFromPath(string path)
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
                Platform.Windows => "windows",
                Platform.Linux => "unix",
                Platform.MacOS => "osx",
                _ => throw new NotSupportedException($"Unsupported OS: {target.Platform}")
            };

            return IO.LibraryFileName($"{baseName}_{os}_{target.Architecture}");
        }
    }
}