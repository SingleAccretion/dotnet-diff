using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.IO;
using System.Threading.Tasks;

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
            static Option<string> FrameworkInstallOption(string itemName) => new(new[] { "-f", "--framework" }, "latest", $"The framework version for the {itemName}, e. g. 5.0, 6.0, latest");
            static Option<string[]> RuntimesInstallOption(string itemName) => new(new[] { "-r", "--runtimes" }, new[] { RuntimeIdentifier.Host.ToString() }, $"The runtime identifiers for the {itemName} to target, e. g. win-x86, linux-x64");

            IO.EnsureExists(AppDir);
            IO.EnsureExists(Sdk.PathToSDKInstalls);

            var installJitutils = new Command("jitutils", "Sets up the dotnet/jitutils repository for later use by dotnet-diff")
            {
                Handler = CommandHandler.Create<IConsole>(InstallJitutilsHandler)
            };

            var installSdk = new Command("sdk", "Installs the bits of the SDK necessary for dotnet-diff to work")
            {
                FrameworkInstallOption("SDK"),
                RuntimesInstallOption("SDK")
            };
            installSdk.Handler = CommandHandler.Create<string, string[], IConsole>(InstallSdkHandler);

            var installJit = new Command("jit", "Installs the Jits for the specified targets")
            {
                FrameworkInstallOption("Jit"),
                RuntimesInstallOption("Jit")
            };
            installJit.Handler = CommandHandler.Create<string, string[], IConsole>(InstallJitHandler);

            var installRuntimeAssemblies = new Command("runtime-assemblies", "Installs the runtime assemblies for the specified targets")
            {
                FrameworkInstallOption("runtime assemblies"),
                RuntimesInstallOption("runtime assemblies")
            };
            installRuntimeAssemblies.Handler = CommandHandler.Create<string, string[], IConsole>(InstallRuntimeAssembliesHandler);

            var installCrossgen2 = new Command("crossgen2", "Installs the Crossgen2 compiler")
            {
                FrameworkInstallOption("Crossgen2")
            };
            installCrossgen2.Handler = CommandHandler.Create<string, IConsole>(InstallCrossgen2Handler);

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
                installJit,
                installRuntimeAssemblies,
                installCrossgen2,
                listInstall
            };

            var diffAsm = new Command("asm", "Obtains the difference in assembly code produced for two assemblies")
            {
                new Argument<string>("baseAssembly"),
                new Argument<string>("diffAssembly"),
                new Argument<string[]>("references", () => Array.Empty<string>()),
                new Option<CompilationMode>("--mode", () => CompilationMode.Crossgen)

            };
            diffAsm.Handler = CommandHandler.Create<string, string, string[], CompilationMode, IConsole>(DiffAsmHandler);

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
                Sdk.Resolve(version, Metadata, console).ResolveTarget(target).InstallJit();
            }
        }

        private static void InstallRuntimeAssembliesHandler(string framework, string[] runtimes, IConsole console)
        {
            var version = ParseVersion(framework);
            var targets = ParseRuntimes(runtimes);

            foreach (var target in targets)
            {
                Sdk.Resolve(version, Metadata, console).ResolveTarget(target).InstallRuntimeAssemblies();
            }
        }

        private static void InstallCrossgen2Handler(string framework, IConsole console)
        {
            var version = ParseVersion(framework);

            Sdk.Resolve(version, Metadata, console).InstallCrossgen2();
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
                console.Out.WriteLine($"- SDK for {version}");
                if (metadata.Crossgen2IsAvailable(version))
                {
                    console.Out.WriteLine($" - Crossgen2");
                }
                foreach (var target in metadata.EnumerateTargets(version))
                {
                    if (metadata.RuntimeAssembliesAreAvailable(version, target) ||
                        metadata.JitIsAvailable(version, target))
                    {
                        console.Out.WriteLine($" - {target} target");
                        if (metadata.RuntimeAssembliesAreAvailable(version, target))
                        {
                            console.Out.WriteLine($"  - Runtime assemblies");
                        }
                        if (metadata.JitIsAvailable(version, target))
                        {
                            console.Out.WriteLine($"  - Jit");
                        }
                    }
                }
            }
        }

        private static void DiffAsmHandler(string baseAssembly, string diffAssembly, string[] references, CompilationMode mode, IConsole console)
        {
            IO.EnsureExists(DasmBasePath);

            var sdk = Sdk.Resolve(Sdk.SupportedSdkVersion, Metadata, console);

            var opts = new Crossgen2CompilationOptions
            {
                JitOptions = { ["NgenDisasm"] = "*" }
            };

            var baseTmpFile = Path.GetTempFileName();
            var diffTmpFile = Path.GetTempFileName();

            var crossgen2 = sdk.ResolveCrossgen2();
            var baseStream = crossgen2.BeginCompilation(new[] { new AssemblyObject(baseAssembly) }, baseTmpFile, opts, sdk, console);
            var diffStream = crossgen2.BeginCompilation(new[] { new AssemblyObject(diffAssembly) }, diffTmpFile, opts, sdk, console);

            // JitAnalyzeAsmDiffer.Diff(baseStream, diffStream, new(Path.GetFileName(baseAssembly), Path.GetFileName(diffAssembly), DasmBasePath), sdk.JitAnalyze);

            if (Config.DeleteTempFiles)
            {
                File.Delete(baseTmpFile);
                File.Delete(diffTmpFile);
            }
        }
    }
}
