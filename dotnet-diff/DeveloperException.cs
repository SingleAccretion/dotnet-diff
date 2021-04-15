using System;

namespace DotnetDiff
{
    // Thrown when an irrecoverable error occured that is a result of a bug.
    public class DeveloperException : Exception
    {
        public DeveloperException(string? message) : base(message) { }
    }
}
