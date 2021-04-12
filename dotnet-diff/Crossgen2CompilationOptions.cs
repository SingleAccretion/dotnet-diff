using System.Collections.Generic;

namespace DotnetDiff
{
    public record Crossgen2CompilationOptions
    {
        public Dictionary<string, string> JitOptions { get; init; } = new Dictionary<string, string>();
        public RuntimeIdentifier Target { get; init; } = RuntimeIdentifier.Host;
        public int Parallelism { get; set; } = 1;
        public bool CompileNoMethods { get; set; }
    }
}