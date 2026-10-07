using System;

namespace IberiaSmartDisc.Core.Manifest
{
    public enum ManifestError
    {
        Malformed,
        NotIberiaDisc,
        UnsupportedFormat,
        InvalidField,
        TooLarge,
        Encoding,
    }

    public sealed class ManifestException : Exception
    {
        public ManifestException(ManifestError error, string message, string? field = null, Exception? inner = null)
            : base(message, inner)
        {
            Error = error;
            Field = field;
        }

        public ManifestError Error { get; }

        /// <summary>Campo JSON afectado (p. ej. "discLaunch.path"), si se conoce.</summary>
        public string? Field { get; }
    }
}
