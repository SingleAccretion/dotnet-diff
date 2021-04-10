using System.Collections.Generic;
using System.IO;

namespace DotnetDiff
{
    public class AsmStream
    {
        private readonly StreamReader _stream;

        public static AsmStream ForAssembly(AssemblyObject assembly, Sdk sdk)
        {
            var stdOut = sdk.Crossgen2.Compile(new[] { assembly }, sdk, new Crossgen2CompilationOptions
            {
                JitOptions = new Dictionary<string, string>
                {
                    { "JitDisasm", "1" }
                }
            });

            return new(stdOut);
        }

        private AsmStream(StreamReader stream)
        {
            _stream = stream;
        }
    }
}