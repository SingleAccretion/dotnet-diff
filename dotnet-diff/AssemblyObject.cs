using System;
using System.Collections.Generic;

namespace DotnetDiff
{
    public class AssemblyObject
    {
        public AssemblyObject(string path)
        {
            Path = path;
            Dependencies = Array.Empty<AssemblyObject>();
        }

        public string Path { get; }
        public IReadOnlyList<AssemblyObject> Dependencies { get; }
    }
}
