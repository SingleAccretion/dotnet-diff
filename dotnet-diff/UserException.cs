using System;

namespace DotnetDiff
{
    // Thrown when an irrecoverable error occured that should be shown to the user.
    public class UserException : Exception
    {
        public UserException(string? message) : base(message) { }
    }
}
