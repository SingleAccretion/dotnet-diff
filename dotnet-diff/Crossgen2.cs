using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DotnetDiff
{
    public sealed class Crossgen2
    {
        private readonly string _path;

        public static Crossgen2 CreateFromPath(string path)
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

        public StreamReader Compile(AssemblyObject[] assemblies, Sdk sdk, Crossgen2CompilationOptions options)
        {
            var arch = options.TargetArchitecture;
            var os = options.TargetPlatform;
            var jit = sdk.JitForRid(new(os, arch));
            var jitOpts = string.Join(' ', options.JitOptions.Select(x => $"--codegenopt {x.Key}={x.Value}"));
            var targets = string.Join(' ', assemblies.Select(x => @$"""{x.Path}"""));
            var refs = string.Join(' ', assemblies.SelectMany(x => x.Dependencies).Distinct().Select(x => $@"""{x.Path}"""));

            var startInfo = new ProcessStartInfo
            {
                FileName = _path,
                WorkingDirectory = Path.GetDirectoryName(_path),
                Arguments = $"{targets} --reference {refs} --targetarch {arch} --targetos {os} --jitpath {jit.Path} {jitOpts}",
                RedirectStandardOutput = true
            };

            return Process.Start(startInfo)?.StandardOutput ?? throw new Exception($"Failed to start the crossgen2 compiler");
        }
    }
}
