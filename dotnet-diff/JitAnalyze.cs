using System;
using System.Diagnostics;
using System.IO;

namespace DotnetDiff
{
    public class JitAnalyze
    {
        private readonly string _path;

        private JitAnalyze(string path) => _path = path;

        public static JitAnalyze FromPath(string path)
        {
            if (Path.GetFileName(path) != IO.ExecutableFileName("jit-analyze"))
            {
                throw new Exception($"Not a valid jit-analyze apth: '{path}'");
            }

            return new(path);
        }

        public StreamReader Analyze(string basePath, string diffPath, bool recursive = true)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _path,
                Arguments = $" - b { basePath } -d { diffPath } { (recursive ? "-r" : "") }",
                RedirectStandardOutput = true,

            };

            var process = new Process()
            {
                StartInfo = startInfo
            };
            process.Start();

            return process.StandardOutput;
        }
    }
}