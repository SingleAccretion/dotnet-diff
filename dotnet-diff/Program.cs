using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.IO;

namespace DotnetDiff
{
    public class Program
    {
        private const string AppId = "dotnet-diff-" + AppVersion;
        private const string MdFileName = "dotnet-diff.json";

        public const string AppVersion = "0.0.1";
        public static string AppDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
        public static ProgramMetadata Metadata { get; } = ProgramMetadata.Open(MdFilePath);

        private static string JitUtilsDirPath { get; } = Path.Combine(AppDir, "jitutils");
        private static string JitUtilsBinPath { get; } = Path.Combine(JitUtilsDirPath, "bin");
        private static string DasmBasePath { get; } = Path.Combine(AppDir, "dasm");

        private static string MdFilePath => Path.Combine(AppDir, MdFileName);

        private static int Main(string[] args)
        {
            static Option<string> FrameworkInstallOption(string itemName) => new Option<string>(new[] { "-f", "--framework" }, "latest", $"The framework version for the {itemName}, e. g. 5.0, 6.0, latest");
            static Option<string[]> RuntimesInstallOption(string itemName) => new Option<string[]>(new[] { "-r", "--runtimes" }, new[] { RuntimeIdentifier.Host.ToString() }, $"The runtime identifiers for the {itemName} to target, e. g. win-x86, linux-x64");

            IO.EnsureExists(AppDir);
            IO.EnsureExists(Sdk.PathToSDKInstalls);

            var installJitutils = new Command("jitutils", "Sets up the dotnet/jitutils repository for later use by dotnet-diff")
            {
                Handler = CommandHandler.Create<IConsole>(InstallJitutilsHandler)
            };

            var installSdk = new Command("sdk", "Installs the bits of the SDK necessary for dotnet-diff to work")
            {
                FrameworkInstallOption("SDK"),
                RuntimesInstallOption("Jit")
            };
            installSdk.Handler = CommandHandler.Create<string, string[], IConsole>(InstallSdkHandler);

            var installJits = new Command("jit", "Installs the Jits for the specified targets")
            {
                FrameworkInstallOption("Jit"),
                RuntimesInstallOption("Jit")
            };
            installJits.Handler = CommandHandler.Create<string, string[], IConsole>(InstallJitHandler);

            var listSdkInstall = new Command("sdks", "Lists the SDKs installed by dotnet-diff")
            {
                Handler = CommandHandler.Create<IConsole>(InstallListSdksHandler)
            };

            var listInstall = new Command("list", "Lists the resources installed by dotnet-diff")
            {
                listSdkInstall
            };

            var install = new Command("install", "Installs various things that dotnet-diff needs")
            {
                installJitutils,
                installSdk,
                installJits,
                listInstall
            };

            var diffAsm = new Command("asm", "Obtains the difference in assembly code produced for two assemblies")
            {
                new Argument<string>("baseAssembly"),
                new Argument<string>("newAssembly"),
                new Argument<string[]>("references", () => Array.Empty<string>()),
                new Option<CompilationMode>("--mode", () => CompilationMode.Crossgen)

            };
            diffAsm.Handler = CommandHandler.Create<string, string, string[], CompilationMode, IConsole>(DiffAsm);

            var cmd = new RootCommand
            {
                diffAsm,
                install
            };

            int errorCode;
            try
            {
                errorCode = cmd.Invoke(args);
            }
            catch (Exception exception)
            {
                errorCode = exception.HResult;
            }
            finally
            {
                Metadata.Save(MdFilePath);
            }

            return errorCode;
        }

        private static void InstallSdkHandler(string framework, string[] runtimes, IConsole console) => Sdk.Install(ParseVersion(framework), ParseRuntimes(runtimes), Metadata, console);

        private static void InstallJitHandler(string framework, string[] runtimes, IConsole console)
        {
            var version = ParseVersion(framework);
            var targets = ParseRuntimes(runtimes);

            foreach (var target in targets)
            {
                Sdk.InstallJit(version, target, Metadata, console);
            }
        }

        private static FrameworkVersion ParseVersion(string framework)
        {
            if (!framework.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Only the latest framework is supported at the moment");

            }

            return Sdk.SupportedSdkVersion;
        }

        private static RuntimeIdentifier[] ParseRuntimes(string[] runtimes) => Array.ConvertAll(runtimes, RuntimeIdentifier.Parse);

        private static void InstallJitutilsHandler(IConsole console)
        {
            IO.EnsureDeletion(JitUtilsDirPath);

            Process.StartProcess("git", "clone https://github.com/dotnet/jitutils.git", AppDir, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();

            var buildScript = Path.Combine(JitUtilsDirPath, "build" + (OperatingSystem.IsWindows() ? ".cmd" : ".sh"));
            Process.StartProcess(buildScript, "-b Release", JitUtilsDirPath, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();
            Process.StartProcess(buildScript, "-p", JitUtilsDirPath, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();

            Metadata.JitUtilsSetUp = true;
        }

        private static void InstallListSdksHandler(IConsole console)
        {
            var metadata = Metadata;

            console.Out.WriteLine($"Install location: '{AppDir}'");
            foreach (var version in metadata.EnumerateSdks())
            {
                console.Out.WriteLine($"SDK for .NET {version}");
                foreach (var target in metadata.EnumerateTargets(version))
                {
                    console.Out.WriteLine($" - Targeting {target}");
                }
            }
        }

        private static void DiffAsm(string baseAssembly, string newAssembly, string[] references, CompilationMode mode, IConsole console)
        {
            IO.EnsureExists(DasmBasePath);

            var dir = Path.Combine(DasmBasePath, $"{Path.GetFileName(baseAssembly)}-vs-{Path.GetFileName(newAssembly)}-{Guid.NewGuid()}");
            var jitDasm = Path.Combine(JitUtilsBinPath, "jit-dasm.exe");
            var jitAnalyze = Path.Combine(JitUtilsBinPath, "jit-analyze.exe");

            // var sdk = Sdk.Resolve(FrameworkVersion.BestGuessSdkVersionForAssembly(baseAssembly), Metadata, console);

            var platform = @"C:\Users\Accretion\source\dotnet\build\CustomCoreRoot";
            var crossgen = @"C:\Users\Accretion\source\dotnet\runtime\artifacts\bin\coreclr\windows.x64.Checked\crossgen2\crossgen2.exe";
            var jit = @"C:\Users\Accretion\source\dotnet\runtime\artifacts\bin\coreclr\windows.x64.Checked\clrjit.dll";

            var baseOut = Path.Combine(dir, "base");
            var diffOut = Path.Combine(dir, "diff");

            var cmd = $"-p {platform} -c {crossgen} -o {baseOut} -j {jit} {baseAssembly}";
            Process.StartProcess(jitDasm, cmd, JitUtilsBinPath, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();

            cmd = cmd.Replace(baseOut, diffOut);
            cmd = cmd.Replace(baseAssembly, newAssembly);
            Process.StartProcess(jitDasm, cmd, JitUtilsBinPath, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();

            cmd = $"-b {baseOut} -d {diffOut} -r";
            Process.StartProcess(jitAnalyze, cmd, JitUtilsBinPath, console.Out.WriteLine, console.Error.WriteLine).WaitForExit();

            // EnsureDeletion(dir);
        }
    }
}
