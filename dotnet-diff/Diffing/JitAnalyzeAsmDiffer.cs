using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Threading.Tasks;

namespace DotnetDiff
{
    public static class JitAnalyzeAsmDiffer
    {
        public static void Diff(Stream baseStream, Stream diffStream, DiffMetadata diffMetadata, JitAnalyze jitAnalyze, IConsole console)
        {
            IO.EnsureExists(diffMetadata.BaseDirectory);
            IO.EnsureExists(diffMetadata.DiffDirectory);

            void CollectStdOut(Stream stdOut, string filePath)
            {
                console.WriteLineDebug($"Started writing to: {filePath}");

                using var fileStream = File.OpenWrite(filePath);
                stdOut.CopyTo(fileStream);

                console.WriteLineDebug($"Finished writing to {filePath}");
            }

            Task.WaitAll(
                Task.Run(() => CollectStdOut(baseStream, diffMetadata.BaseDasmPath)),
                Task.Run(() => CollectStdOut(diffStream, diffMetadata.DiffDasmPath)));

            var stdOut = jitAnalyze.Analyze(diffMetadata.BaseDasmPath, diffMetadata.DiffDasmPath);

            while (stdOut.ReadLine() is string line)
            {
                console.Out.WriteLine(line);
            }
        }
    }
}
