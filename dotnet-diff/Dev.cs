using System.Diagnostics.CodeAnalysis;

namespace DotnetDiff
{
    public static class Dev
    {
        public static void Assert([DoesNotReturnIf(false)] bool condition, string? message = null, string? file = null, int line = -1)
        {
#if DEBUG
            var enableAsserts = true;
#else
            var enableAsserts = Config.DebugLog;
#endif
            if (enableAsserts && !condition)
            {
                throw new DeveloperException($"Assetion failed{(message is null ? "" : $": {message}")} in {file}:{line}");
            }
        }
    }
}
