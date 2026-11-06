using System;

namespace Flyweight.Core
{
    /// <summary>A problem worth showing to the user as-is. The message must make sense on its own.</summary>
    public sealed class CompressionException : Exception
    {
        public CompressionException(string message, string details = null) : base(message)
        {
            Details = details;
        }

        /// <summary>What ffmpeg itself said, for anyone who wants to look.</summary>
        public string Details { get; }
    }
}
