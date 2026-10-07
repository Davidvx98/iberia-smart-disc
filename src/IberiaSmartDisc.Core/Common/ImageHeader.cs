using System;

namespace IberiaSmartDisc.Core.Common
{
    public enum ImageKind
    {
        Unknown,
        Png,
        Jpeg,
        Bmp,
    }

    /// <summary>
    /// Lee el tipo y las dimensiones de una imagen por su cabecera, antes de
    /// decodificarla. Así una carátula del disco que en realidad sea un
    /// metarchivo (EMF/WMF), un TIFF o una imagen gigantesca se rechaza sin
    /// llegar a GDI+.
    /// </summary>
    public static class ImageHeader
    {
        public const int MaxSide = 8192;
        public const long MaxPixels = 16L * 1000 * 1000;

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static bool TryRead(byte[] data, out ImageKind kind, out int width, out int height)
        {
            kind = ImageKind.Unknown;
            width = 0;
            height = 0;
            if (data == null) return false;
            if (StartsWith(data, PngSignature)) return ReadPng(data, out kind, out width, out height);
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ReadJpeg(data, out kind, out width, out height);
            if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M') return ReadBmp(data, out kind, out width, out height);
            return false;
        }

        /// <summary>Dimensiones razonables para una carátula: ni vacías ni capaces de agotar la memoria.</summary>
        public static bool IsAcceptableSize(int width, int height) =>
            width > 0 && height > 0 && width <= MaxSide && height <= MaxSide && (long)width * height <= MaxPixels;

        public static ImageKind KindForExtension(string? extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".png": return ImageKind.Png;
                case ".jpg":
                case ".jpeg": return ImageKind.Jpeg;
                case ".bmp": return ImageKind.Bmp;
                default: return ImageKind.Unknown;
            }
        }

        private static bool ReadPng(byte[] data, out ImageKind kind, out int width, out int height)
        {
            kind = ImageKind.Png;
            width = 0;
            height = 0;
            // Firma (8) + longitud (4) + "IHDR" (4) + ancho (4) + alto (4).
            if (data.Length < 24 || data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R') return false;
            long w = BigEndian32(data, 16);
            long h = BigEndian32(data, 20);
            if (w > int.MaxValue || h > int.MaxValue) return false;
            width = (int)w;
            height = (int)h;
            return true;
        }

        private static bool ReadJpeg(byte[] data, out ImageKind kind, out int width, out int height)
        {
            kind = ImageKind.Jpeg;
            width = 0;
            height = 0;
            int i = 2;
            while (i + 3 < data.Length)
            {
                if (data[i] != 0xFF) return false;
                byte marker = data[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }
                if (marker == 0xDA || marker == 0xD9) return false;
                int length = (data[i + 2] << 8) | data[i + 3];
                if (length < 2) return false;
                bool startOfFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (startOfFrame)
                {
                    if (i + 8 >= data.Length) return false;
                    height = (data[i + 5] << 8) | data[i + 6];
                    width = (data[i + 7] << 8) | data[i + 8];
                    return true;
                }
                i += 2 + length;
            }
            return false;
        }

        private static bool ReadBmp(byte[] data, out ImageKind kind, out int width, out int height)
        {
            kind = ImageKind.Bmp;
            width = 0;
            height = 0;
            int headerSize = BitConverter.ToInt32(data, 14);
            if (headerSize == 12)
            {
                width = BitConverter.ToUInt16(data, 18);
                height = BitConverter.ToUInt16(data, 20);
                return true;
            }
            if (headerSize < 40) return false;
            int w = BitConverter.ToInt32(data, 18);
            int h = BitConverter.ToInt32(data, 22);
            if (w <= 0 || h == 0 || h == int.MinValue) return false;
            width = w;
            height = Math.Abs(h);
            return true;
        }

        private static long BigEndian32(byte[] data, int offset) =>
            ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | data[offset + 3];

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i]) return false;
            }
            return true;
        }
    }
}
