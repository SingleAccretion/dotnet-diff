using System.IO;

namespace DotnetDiff
{
    public class DiffMetadata
    {
        public DiffMetadata(string baseAssemblyName, string diffAssemblyName, string savePath)
        {
            BaseDirectory = Path.Combine(savePath, "base");
            BaseDasmPath = Path.Combine(BaseDirectory, $"{baseAssemblyName}.dasm");
            DiffDirectory = Path.Combine(savePath, "diff");
            DiffDasmPath = Path.Combine(DiffDirectory, $"{diffAssemblyName}.dasm");
        }

        public string BaseDasmPath { get; }
        public string BaseDirectory { get; }
        public string DiffDasmPath { get; }
        public string DiffDirectory { get; }
    }
}
