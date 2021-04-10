using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DotnetDiff
{
    public record Crossgen2CompilationOptions
    {
        public IReadOnlyDictionary<string, string> JitOptions { get; init; } = new Dictionary<string, string>();
        public Architecture TargetArchitecture { get; init; } = Architecture.X64;
        public Platform TargetPlatform { get; init; } = Platform.Windows;
    }
}