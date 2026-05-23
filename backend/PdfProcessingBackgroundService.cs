using System.Diagnostics;
using System.Text.Json;
using ImageMagick;
using PdfSharpCore.Pdf.IO;

namespace PDFBackend.Services;

public class PdfProcessingBackgroundService : BackgroundService
{
    private readonly PdfProcessingQueue _queue;
    private readonly ILogger<PdfProcessingBackgroundService> _logger;

    public PdfProcessingBackgroundService(PdfProcessingQueue queue, ILogger<PdfProcessingBackgroundService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var folderPath = await _queue.DequeueAsync(stoppingToken);

            try
            {
                _logger.LogInformation("Processing PDF in folder: {FolderPath}", folderPath);

                var pdfFile = new DirectoryInfo(folderPath).GetFiles("*.pdf").FirstOrDefault();
                if (pdfFile == null) continue;

                var isLinearized = await IsPdfLinearizedAsync(pdfFile.FullName, stoppingToken);
                if (!isLinearized)
                {
                    _logger.LogInformation("PDF is not linearized. Converting with QPDF...");
                    await LinearizePdfAsync(pdfFile.FullName, stoppingToken);
                    _logger.LogInformation("QPDF linearization complete.");
                }

            _logger.LogInformation("Generating thumbnails...");
            await GenerateThumbnailsAsync(pdfFile.FullName, folderPath, stoppingToken);

                // 4. Create document-level metadata
                var metadataPath = Path.Combine(folderPath, "metadata.json");
                var documentMetadata = new { ProcessedAt = DateTime.UtcNow, Linearized = true }; // It is now guaranteed linearized
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(documentMetadata), stoppingToken);

                _logger.LogInformation("Extracting page metadata...");
                await ExtractPageMetadataAsync(pdfFile.FullName, folderPath, stoppingToken);

                _logger.LogInformation("Successfully processed PDF in folder: {FolderPath}", folderPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF in folder: {FolderPath}", folderPath);
            }
        }
    }

    private async Task<bool> IsPdfLinearizedAsync(string filePath, CancellationToken stoppingToken)
    {
        try
        {
            // According to the PDF specification, a linearized PDF must contain
            // the "Linearized" dictionary within the first 1024 bytes.
            // This byte check is nearly instantaneous and avoids spawning a process just to check.
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
            var header = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);

            return header.Contains("Linearized");
        }
        catch
        {
            return false;
        }
    }

    private async Task LinearizePdfAsync(string filePath, CancellationToken stoppingToken)
    {
        var tempFilePath = filePath + ".tmp.pdf";
        var startInfo = new ProcessStartInfo
        {
            FileName = "qpdf",
            Arguments = $"--linearize \"{filePath}\" \"{tempFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start QPDF. Ensure qpdf is installed and added to your system's PATH.");

        await process.WaitForExitAsync(stoppingToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(stoppingToken);
            throw new Exception($"QPDF failed with exit code {process.ExitCode}: {error}");
        }

        // Replace the original file with the linearized version
        File.Delete(filePath);
        File.Move(tempFilePath, filePath);
    }

    private async Task GenerateThumbnailsAsync(string filePath, string folderPath, CancellationToken stoppingToken)
    {
        // Offload CPU-heavy image processing to a background thread
        await Task.Run(() =>
        {
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
                    image.Format = MagickFormat.Png;
                    image.Resize(400, 0); // 400px width, keeping aspect ratio

                    var thumbPath = Path.Combine(folderPath, $"thumbnail.{i + 1}.png");
                    image.Write(thumbPath);
                }
            });
        }, stoppingToken);
    }

    private async Task ExtractPageMetadataAsync(string filePath, string folderPath, CancellationToken stoppingToken)
    {
        // Offload CPU-bound parsing to a background thread
        var pagesMetadata = await Task.Run(() =>
        {
            var metadata = new List<object>();
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.InformationOnly);

            for (int i = 0; i < document.PageCount; i++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var page = document.Pages[i];

                metadata.Add(new
                {
                    PageNumber = i + 1,
                    Width = Math.Round(page.Width.Point, 2),
                    Height = Math.Round(page.Height.Point, 2),
                    Rotation = page.Rotate
                });
            }
            return metadata;
        }, stoppingToken);

        var pagesMetadataPath = Path.Combine(folderPath, "pages-metadata.json");
        await File.WriteAllTextAsync(pagesMetadataPath, JsonSerializer.Serialize(pagesMetadata), stoppingToken);
    }
}