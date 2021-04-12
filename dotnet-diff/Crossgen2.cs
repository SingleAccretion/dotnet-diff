using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DotnetDiff
{
    public sealed class Crossgen2
    {
        private readonly string _path;

        public static Crossgen2 FromPath(string path)
        {
            if (Path.GetFileName(path) is not ("crossgen2" or "crossgen2.exe"))
            {
                throw new Exception($"{path} is not a path to the crossgen2 compiler");
            }

            return new(path);
        }

        private Crossgen2(string path)
        {
            _path = path;
        }

        public StreamReader BeginCompilation(AssemblyObject[] assemblies, string outputPath, Crossgen2CompilationOptions options, Sdk sdk, IConsole console)
        {
            var arch = options.Target.Architecture;
            var os = options.Target.Platform;
            var jit = sdk.ResolveTarget(options.Target).ResolveJit();
            var jitOpts = string.Join(' ', options.JitOptions.Select(x => $"--codegenopt {x.Key}={x.Value}"));
            var targets = string.Join(' ', assemblies.Select(x => @$"""{x.Path}"""));
            var refs = string.Join(" -r", assemblies.SelectMany(x => x.Dependencies).Distinct().Select(x => $@"""{x.Path}"""));
            if (refs.Length is not 0)
            {
                refs = $"-r {refs}";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _path,
                WorkingDirectory = Path.GetDirectoryName(_path),
                Arguments =
                    @$"-o ""{outputPath}"" {refs} --targetarch {arch} --targetos {os} --jitpath {jit.Path} {jitOpts} " +
                    @$"--parallelism {options.Parallelism} {(options.CompileNoMethods ? "--compile-no-methods" : "")} " +
                    $@"{targets}",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            console.WriteLineDebug($"Crossgen2 command line: '{startInfo.FileName} {startInfo.Arguments}'");

            var process = new Process() { StartInfo = startInfo };
            process.ErrorDataReceived += (o, e) =>
            {
                if (e.Data is not null)
                {
                    console.Error.WriteLine($"Crossgen2 failed with: {e.Data}");
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            console.Out.WriteLine($"Started compilation of assemblies with Crossgen2");

            return process.StandardOutput;
        }
    }
}
