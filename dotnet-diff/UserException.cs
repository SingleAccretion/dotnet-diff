using System;

namespace DotnetDiff
{
    public class UserException : Exception
    {
        public UserException(string? message) : base(message) { }
    }
}
