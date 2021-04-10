using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotnetDiff
{
    public record RuntimeAssemblies(IEnumerable<AssemblyObject> Assemblies)
    {
        public static RuntimeAssemblies FromDirectory(string path) => new(Directory.EnumerateFiles(path, "*.dll").Select(x => new AssemblyObject(x)));
    }
}