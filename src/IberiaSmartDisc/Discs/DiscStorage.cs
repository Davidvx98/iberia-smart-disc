using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Logging;

namespace IberiaSmartDisc.Discs
{
    internal static class DiscReader
    {
        /// <summary>
        /// Espera a que la unidad monte el disco (los lectores USB y los Blu-ray
        /// pueden tardar varios segundos) y lee el manifiesto. Solo se llama tras
        /// un aviso de Windows, nunca en bucle.
        /// </summary>
        public static ManifestReadResult WaitAndRead(string root, TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                if (IsReady(root))
                {
                    var result = ManifestReader.Read(root);
                    if (result.Status != ManifestReadStatus.ReadError || clock.Elapsed >= timeout) return result;
                }
                if (clock.Elapsed >= timeout) return ManifestReadResult.NotReady();
                Thread.Sleep(1000);
            }
        }

        public static bool IsDriveRoot(string path) => path.Length == 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

        private static bool IsReady(string root)
        {
            try
            {
                return IsDriveRoot(root) ? new DriveInfo(root).IsReady : Directory.Exists(root);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Copia local de las carátulas para mostrarlas sin el disco (historial,
    /// avisos). Se reescalan a 512 px como máximo.
    /// </summary>
    internal static class CoverCache
    {
        private const long MaxSourceBytes = 8 * 1024 * 1024;
        private const int MaxSize = 512;

        public static string PathFor(string gameId) => Path.Combine(AppPaths.CoversDirectory, gameId + ".png");

        public static void StoreFromDisc(DiscManifest manifest, string discRoot, AppLog log)
        {
            if (manifest.CoverPath == null) return;
            try
            {
                string source = DiscRelativePath.Combine(discRoot, manifest.CoverPath);
                var info = new FileInfo(source);
                if (!info.Exists || info.Length > MaxSourceBytes) return;
                byte[] bytes = File.ReadAllBytes(source);

                // Antes de llegar a GDI+: tipo real por la cabecera (no por la extensión),
                // solo PNG/JPEG/BMP y dimensiones razonables. Así no se reproducen
                // metarchivos ni se decodifican imágenes gigantescas.
                var expected = ImageHeader.KindForExtension(Path.GetExtension(source));
                if (!ImageHeader.TryRead(bytes, out var kind, out var headerWidth, out var headerHeight)
                    || kind != expected
                    || !ImageHeader.IsAcceptableSize(headerWidth, headerHeight))
                {
                    log.Warn("Carátula de " + manifest.GameId + " rechazada: no es una imagen PNG, JPEG o BMP válida y de tamaño razonable.");
                    return;
                }

                Directory.CreateDirectory(AppPaths.CoversDirectory);
                using (var stream = new MemoryStream(bytes))
                using (var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true))
                {
                    if (image is Metafile || !IsExpectedFormat(image.RawFormat, kind) || image.Width != headerWidth || image.Height != headerHeight) return;
                    double scale = Math.Min(1.0, (double)MaxSize / Math.Max(image.Width, image.Height));
                    int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                    using (var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(image, 0, 0, width, height);
                        }
                        string target = PathFor(manifest.GameId);
                        string temporary = target + ".tmp";
                        resized.Save(temporary, ImageFormat.Png);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(temporary, target);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is ManifestException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)
            {
                log.Warn("No se pudo guardar la carátula de " + manifest.GameId + ": " + ex.GetType().Name);
            }
        }

        private static bool IsExpectedFormat(ImageFormat format, ImageKind kind)
        {
            switch (kind)
            {
                case ImageKind.Png: return format.Guid == ImageFormat.Png.Guid;
                case ImageKind.Jpeg: return format.Guid == ImageFormat.Jpeg.Guid;
                case ImageKind.Bmp: return format.Guid == ImageFormat.Bmp.Guid || format.Guid == ImageFormat.MemoryBmp.Guid;
                default: return false;
            }
        }

        /// <summary>Carga la carátula sin bloquear el archivo. Quien la recibe la libera.</summary>
        public static Image? Load(string gameId)
        {
            try
            {
                string path = PathFor(gameId);
                if (!File.Exists(path)) return null;
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException)
            {
                return null;
            }
        }

        public static void Delete(string gameId)
        {
            try
            {
                File.Delete(PathFor(gameId));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Copia del último manifiesto de cada juego, para poder cambiar su
    /// instalación desde la configuración sin tener el disco puesto.
    /// </summary>
    internal static class ManifestCache
    {
        private static string PathFor(string gameId) => Path.Combine(AppPaths.ManifestsDirectory, gameId + ".json");

        public static void Store(DiscManifest manifest, AppLog log)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ManifestsDirectory);
                File.WriteAllText(PathFor(manifest.GameId), manifest.RawJson, new UTF8Encoding(false));
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                log.Warn("No se pudo guardar la copia del manifiesto: " + ex.Message);
            }
        }

        public static DiscManifest? Load(string gameId)
        {
            try
            {
                string path = PathFor(gameId);
                if (!File.Exists(path) || new FileInfo(path).Length > DiscManifest.MaxFileBytes) return null;
                var manifest = ManifestParser.Parse(File.ReadAllText(path, Encoding.UTF8));
                return manifest.GameId == gameId ? manifest : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ManifestException)
            {
                return null;
            }
        }

        public static void Delete(string gameId)
        {
            try
            {
                File.Delete(PathFor(gameId));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
