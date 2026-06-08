using ImageMagick;
using PdfiumViewer;
using System.Linq;
using PdfSharpCore.Pdf.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PDFBackend.Services
{
    public static class PdfThumbnailService
    {
        #region PdfiumViewer version
        public static async Task GenerateThumbnailsPdfiumViewerAsync(
            string pdfPath,
            string outputFolder,
            int thumbWidth = 300,
            int thumbHeight = 300,
            int dpi = 150,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found", pdfPath);

            Directory.CreateDirectory(outputFolder);

            using var document = PdfDocument.Load(pdfPath);
            int pageCount = document.PageCount;

            progress?.Report($"Loaded PDF: {pageCount} pages");

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Rendering page {pageIndex + 1}/{pageCount}");

                // Render page → image
                using var pageImage = document.Render(
                    pageIndex,
                    dpi,
                    dpi,
                    true // include annotations
                );

                // Downscale → thumbnail
                using var thumb = CreateThumbnail(pageImage, thumbWidth, thumbHeight);

                string fileName = Path.Combine(
                    outputFolder,
                    $"thumbnail.{pageIndex + 1}.jpg"
                );

                // Async file write
                await SaveJpegAsync(thumb, fileName, 85, cancellationToken);
            }

            progress?.Report("Thumbnail generation completed");
        }

        /// <summary>
        /// Parallel version: processes multiple pages concurrently for better performance on complex PDFs.
        /// Loads document ONCE and renders pages in parallel with a lock (PdfiumViewer is not fully thread-safe).
        /// </summary>
        public static async Task GenerateThumbnailsPdfiumViewerParallelAsync(
            string pdfPath,
            string outputFolder,
            int thumbWidth = 300,
            int thumbHeight = 300,
            int dpi = 150,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            IReadOnlyList<int>? pageList = null)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found", pdfPath);

            Directory.CreateDirectory(outputFolder);

            // Load document once and keep it open for all parallel renders
            using var document = PdfDocument.Load(pdfPath);
            int pageCount = document.PageCount;

            progress?.Report($"Loaded PDF: {pageCount} pages");

            // Lock around render operations (PdfiumViewer is not fully thread-safe)
            var renderLock = new object();
            // Save operations use GDI+ and are not fully thread-safe across threads.
            // Serialize calls to Image.Save to avoid intermittent GDI+ ArgumentException.
            var saveLock = new object();

            // Use Parallel.For with CPU-count parallelism for rendering
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                //MaxDegreeOfParallelism = 1,
                CancellationToken = cancellationToken
            };

            await Task.Run(() =>
            {
                // Determine which zero-based page indices to process
                IEnumerable<int> indices;
                if (pageList == null || pageList.Count == 0)
                {
                    indices = Enumerable.Range(0, pageCount);
                }
                else
                {
                    // Convert 1-based page numbers to 0-based indices and filter invalid values
                    indices = pageList
                        .Where(p => p >= 1)
                        .Select(p => p - 1)
                        .Distinct()
                        .Where(i => i >= 0 && i < pageCount)
                        .ToArray();
                }

                // If there are no pages to process, just return
                if (!indices.Any())
                    return;

                Parallel.ForEach(indices, parallelOptions, pageIndex =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report($"Rendering page {pageIndex + 1}/{pageCount}");

                    // Render page → image (locked to ensure thread-safety)
                    Image pageImage;
                    lock (renderLock)
                    {
                        pageImage = document.Render(
                            pageIndex,
                            dpi,
                            dpi,
                            true // include annotations
                        );
                    }

                    // Thumbnail creation and JPEG encoding can run in parallel (outside lock)
                    using (pageImage)
                    {
                        using var thumb = CreateThumbnail(pageImage, thumbWidth, thumbHeight);

                        if (thumb == null)
                            throw new InvalidOperationException("CreateThumbnail returned null.");

                        if (thumb.Width <= 0 || thumb.Height <= 0)
                            throw new InvalidOperationException($"Invalid thumbnail dimensions: {thumb.Width}x{thumb.Height}");

                        string fileName = Path.Combine(outputFolder, $"thumbnail.{pageIndex + 1}.jpg");

                        var encoder = ImageCodecInfo.GetImageEncoders()
                            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

                        if (encoder == null)
                            throw new InvalidOperationException("JPEG encoder not found.");

                        using var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);

                        lock (saveLock)
                        {
                            thumb.Save(fileName, encoder, encoderParams);
                        }
                    }
                });
            }, cancellationToken);

            progress?.Report("Thumbnail generation completed");
        }

        private static Bitmap CreateThumbnail(Image source, int maxWidth, int maxHeight)
        {
            double ratioX = (double)maxWidth / source.Width;
            double ratioY = (double)maxHeight / source.Height;
            double ratio = Math.Min(ratioX, ratioY);

            int newWidth = (int)(source.Width * ratio);
            int newHeight = (int)(source.Height * ratio);

            var thumb = new Bitmap(newWidth, newHeight);
            thumb.SetResolution(source.HorizontalResolution, source.VerticalResolution);

            using var g = Graphics.FromImage(thumb);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.DrawImage(source, 0, 0, newWidth, newHeight);

            return thumb;
        }

        private static async Task SaveJpegAsync(
            Bitmap bmp,
            string path,
            long quality,
            CancellationToken token)
        {
            var encoder = ImageCodecInfo.GetImageDecoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);

            using var ms = new MemoryStream();
            bmp.Save(ms, encoder, encoderParams);
            ms.Position = 0;

            await File.WriteAllBytesAsync(path, ms.ToArray(), token);
        }
        #endregion PdfiumViewer version

        #region Ghostscript version
        private static readonly SemaphoreSlim GhostscriptSemaphore = new SemaphoreSlim(2, 2); // Max 2 concurrent Ghostscript operations

        public static async Task GenerateThumbnailsGhostscriptFastAsync(string filePath, string folderPath, CancellationToken stoppingToken)
        {
            // Offload CPU-heavy image processing to a background thread
            await Task.Run(() =>
            {
                // If Ghostscript is bundled locally, tell Magick.NET where to find it.
                var localGsPath = Path.Combine(AppContext.BaseDirectory, "ghostscript");
                if (Directory.Exists(localGsPath))
                {
                    MagickNET.SetGhostscriptDirectory(localGsPath);
                }

                // Get page count using PdfSharpCore to avoid loading all images into RAM at once
                using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.InformationOnly);
                int pageCount = document.PageCount;

                // Limit the degree of parallelism to prevent OutOfMemory exceptions on massive PDFs.
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                    CancellationToken = stoppingToken
                };

                Parallel.For(0, pageCount, parallelOptions, i =>
                {
                    // Read only a single page at a time to handle 2800+ page PDFs safely
                    var settings = new MagickReadSettings
                    {
                        Density = new Density(72),
                        FrameIndex = (uint?)i,
                        FrameCount = 1
                    };

                    using var images = new MagickImageCollection();
                    images.Read(filePath, settings);

                    if (images.Count > 0)
                    {
                        var image = images[0];
                        // Replace transparent PDF backgrounds with white before converting to JPEG
                        image.BackgroundColor = MagickColors.White;
                        image.Alpha(AlphaOption.Remove);

                        image.Format = MagickFormat.Jpeg;
                        image.Quality = 60; // Lower JPEG quality to significantly reduce file size
                        image.Strip(); // Remove hidden color profiles and metadata
                        image.Resize(400, 0); // 400px width, keeping aspect ratio

                        var thumbPath = Path.Combine(folderPath, $"thumbnail.{i + 1}.jpg");
                        image.Write(thumbPath);
                    }
                });
            }, stoppingToken);
        }

        public static async Task GenerateThumbnailsGhostscriptStableAsync(string filePath, string folderPath, ILogger logger,  CancellationToken stoppingToken)
        {
            // Offload CPU-heavy image processing to a background thread
            await Task.Run(() =>
            {
                // If Ghostscript is bundled locally, tell Magick.NET where to find it.
                var localGsPath = Path.Combine(AppContext.BaseDirectory, "ghostscript");
                if (Directory.Exists(localGsPath))
                {
                    MagickNET.SetGhostscriptDirectory(localGsPath);
                }

                // Get page count using PdfSharpCore to avoid loading all images into RAM at once
                using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.InformationOnly);
                int pageCount = document.PageCount;

                var parallelOptions = new ParallelOptions
                {
                    // Limit the degree of parallelim to prevent OutOfMemory exceptions on massive PDFs
                    //MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),

                    // Process pages sequentially to avoid overwhelming Ghostscript
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 1),
                    CancellationToken = stoppingToken
                };

                Parallel.For(0, pageCount, parallelOptions, i =>
                {
                    // Limit concurrent Ghostscript operations globally across all PDFs
                    GhostscriptSemaphore.Wait(stoppingToken);

                    try
                    {
                        // Read only a single page at a time to handle 2800+ page PDFs safely
                        var settings = new MagickReadSettings
                        {
                            Density = new Density(72),
                            FrameIndex = (uint?)i,
                            FrameCount = 1
                        };

                        using var images = new MagickImageCollection();
                        images.Read(filePath, settings);

                        if (images.Count > 0)
                        {
                            var image = images[0];
                            // Replace transparent PDF backgrounds with white before converting to JPEG
                            image.BackgroundColor = MagickColors.White;
                            image.Alpha(AlphaOption.Remove);

                            image.Format = MagickFormat.Jpeg;
                            image.Quality = 60; // Lower JPEG quality to significantly reduce file size
                            image.Strip(); // Remove hidden color profiles and metadata
                            image.Resize(400, 0); // 400px width, keeping aspect ratio

                            var thumbPath = Path.Combine(folderPath, $"thumbnail.{i + 1}.jpg");
                            image.Write(thumbPath);
                        }
                    }
                    catch (MagickDelegateErrorException ex)
                    {
                        logger.LogWarning(ex, "Failed to generate thumbnail for page {PageNumber}. Skipping.", i + 1);
                    }
                    finally
                    {
                        GhostscriptSemaphore.Release();
                    }


                });
            }, stoppingToken);
        }
        #endregion Ghostscript version
    }
}
