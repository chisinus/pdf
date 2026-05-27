using ImageMagick;
using PdfSharpCore.Pdf.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

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

                var currentProcess = Process.GetCurrentProcess();
                var initialMemory = currentProcess.WorkingSet64;
                double linearizationTimeSec = 0;
                double linearizationMemoryChangeMb = 0;

                var isLinearized = await IsPdfLinearizedAsync(pdfFile.FullName, stoppingToken);
                if (!isLinearized)
                {
                    _logger.LogInformation("PDF is not linearized. Converting with QPDF...");
                    var linWatch = Stopwatch.StartNew();
                    var preLinMemory = currentProcess.WorkingSet64;

                    await LinearizePdfAsync(pdfFile.FullName, stoppingToken);

                    linWatch.Stop();
                    currentProcess.Refresh();
                    linearizationTimeSec = linWatch.Elapsed.TotalSeconds;
                    linearizationMemoryChangeMb = (currentProcess.WorkingSet64 - preLinMemory) / (1024.0 * 1024.0);

                    _logger.LogInformation("QPDF linearization complete in {ElapsedSeconds:F2}s. Memory change: {MemoryChange:F2} MB.", linearizationTimeSec, linearizationMemoryChangeMb);
                }

                _logger.LogInformation("Generating thumbnails...");
                var thumbWatch = Stopwatch.StartNew();
                var preThumbMemory = currentProcess.WorkingSet64;

                //await PdfThumbnailService.GenerateThumbnailsGhostscriptFastAsync(pdfFile.FullName, folderPath, stoppingToken);
                //await PdfThumbnailService.GenerateThumbnailsGhostscriptStableAsync(pdfFile.FullName, folderPath, _logger, stoppingToken);
                await PdfThumbnailService.GenerateThumbnailsPdfiumViewerAsync(pdfFile.FullName, folderPath, 300, 300, 150, null, stoppingToken);

                thumbWatch.Stop();

                currentProcess.Refresh();
                var thumbMemoryChangeMb = (currentProcess.WorkingSet64 - preThumbMemory) / (1024.0 * 1024.0);
                _logger.LogInformation("Thumbnails generated in {ElapsedSeconds:F2}s. Memory change: {MemoryChange:F2} MB.", thumbWatch.Elapsed.TotalSeconds, thumbMemoryChangeMb);

                _logger.LogInformation("Extracting page metadata...");
                var metaWatch = Stopwatch.StartNew();
                var preMetaMemory = currentProcess.WorkingSet64;
                await ExtractPageMetadataAsync(pdfFile.FullName, folderPath, stoppingToken);
                metaWatch.Stop();

                currentProcess.Refresh();
                var metaMemoryChangeMb = (currentProcess.WorkingSet64 - preMetaMemory) / (1024.0 * 1024.0);
                _logger.LogInformation("Page metadata extracted in {ElapsedSeconds:F2}s. Memory change: {MemoryChange:F2} MB.", metaWatch.Elapsed.TotalSeconds, metaMemoryChangeMb);

                // 4. Create document-level metadata
                var metadataPath = Path.Combine(folderPath, "metadata.json");
                var documentMetadata = new { ProcessedAt = DateTime.UtcNow, Linearized = true };
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(documentMetadata), stoppingToken);

                // 5. Save performance tracking data
                var performancePath = Path.Combine(folderPath, "performance.json");
                var performanceData = new
                {
                    LinearizationTimeSec = Math.Round(linearizationTimeSec, 2),
                    LinearizationMemoryChangeMb = Math.Round(linearizationMemoryChangeMb, 2),
                    ThumbnailGenerationTimeSec = Math.Round(thumbWatch.Elapsed.TotalSeconds, 2),
                    ThumbnailMemoryChangeMb = Math.Round(thumbMemoryChangeMb, 2),
                    MetadataGenerationTimeSec = Math.Round(metaWatch.Elapsed.TotalSeconds, 2),
                    MetadataMemoryChangeMb = Math.Round(metaMemoryChangeMb, 2),
                    TotalMemoryChangeMb = Math.Round((currentProcess.WorkingSet64 - initialMemory) / (1024.0 * 1024.0), 2)
                };
                await File.WriteAllTextAsync(performancePath, JsonSerializer.Serialize(performanceData), stoppingToken);

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

        // Look for qpdf in a local 'qpdf' folder first, fallback to system PATH
        var localQpdfPath = Path.Combine(AppContext.BaseDirectory, "qpdf", "qpdf.exe");
        var executable = File.Exists(localQpdfPath) ? localQpdfPath : "qpdf";

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--linearize \"{filePath}\" \"{tempFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start QPDF. Tried path: {executable}. Ensure qpdf is installed or placed in the qpdf folder.", ex);
        }

        if (process == null)
        {
            throw new InvalidOperationException("Failed to start QPDF process (returned null).");
        }

        using (process)
        {
            await process.WaitForExitAsync(stoppingToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(stoppingToken);
                throw new Exception($"QPDF failed with exit code {process.ExitCode}: {error}");
            }
        }

        // Replace the original file with the linearized version
        File.Delete(filePath);
        File.Move(tempFilePath, filePath);
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