using System;
using System.IO;
using System.Text;

namespace IberiaSmartDisc.Core.Manifest
{
    public enum ManifestReadStatus
    {
        Ok,

        /// <summary>El disco no tiene iberia-disc.json: no es un disco nuestro y se ignora.</summary>
        NotFound,

        /// <summary>La unidad no terminó de montar el disco a tiempo.</summary>
        NotReady,

        /// <summary>Hay manifiesto, pero no se pudo leer (disco expulsado, rayado…).</summary>
        ReadError,

        /// <summary>Hay manifiesto, pero no es válido.</summary>
        Invalid,
    }

    public sealed class ManifestReadResult
    {
        private ManifestReadResult(ManifestReadStatus status, DiscManifest? manifest, string? message, ManifestException? error)
        {
            Status = status;
            Manifest = manifest;
            Message = message;
            Error = error;
        }

        public ManifestReadStatus Status { get; }

        public DiscManifest? Manifest { get; }

        public string? Message { get; }

        public ManifestException? Error { get; }

        public static ManifestReadResult Ok(DiscManifest manifest) => new ManifestReadResult(ManifestReadStatus.Ok, manifest, null, null);

        public static ManifestReadResult NotFound() => new ManifestReadResult(ManifestReadStatus.NotFound, null, null, null);

        public static ManifestReadResult NotReady() => new ManifestReadResult(ManifestReadStatus.NotReady, null, "La unidad no está lista.", null);

        public static ManifestReadResult ReadError(string message) => new ManifestReadResult(ManifestReadStatus.ReadError, null, message, null);

        public static ManifestReadResult Invalid(ManifestException error) =>
            new ManifestReadResult(ManifestReadStatus.Invalid, null, error.Message, error);
    }

    public static class ManifestReader
    {
        public static ManifestReadResult Read(string discRoot)
        {
            string path = Path.Combine(discRoot, DiscManifest.FileName);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return ManifestReadResult.NotFound();
                if (info.Length > DiscManifest.MaxFileBytes)
                {
                    return ManifestReadResult.Invalid(new ManifestException(ManifestError.TooLarge, "El manifiesto supera los 64 KB."));
                }

                byte[] bytes = ReadLimited(path, DiscManifest.MaxFileBytes);
                string json;
                try
                {
                    json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return ManifestReadResult.Invalid(new ManifestException(ManifestError.Encoding, "El manifiesto no está guardado en UTF-8."));
                }
                return ManifestReadResult.Ok(ManifestParser.Parse(json));
            }
            catch (ManifestException ex)
            {
                return ManifestReadResult.Invalid(ex);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ManifestReadResult.ReadError(ex.Message);
            }
        }

        private static byte[] ReadLimited(string path, int maxBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes) throw new ManifestException(ManifestError.TooLarge, "El manifiesto supera los 64 KB.");
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }
    }
}
