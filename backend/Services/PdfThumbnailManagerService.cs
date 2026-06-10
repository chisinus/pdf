using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PDFBackend.Services;

public class PdfThumbnailManagerService : IPdfThumbnailManagerService
{
    private readonly string _uploadFolder;

    public PdfThumbnailManagerService()
    {
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    }

    private static bool IsTemporaryPdf(FileInfo file) =>
        file.Name.EndsWith(".tmp.pdf", StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(".qpdf.pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> GetThumbnailAsync(string id, int page, CancellationToken cancellationToken)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath))
            return null;

        await EnsureThumbnailsExistAsync(folderPath, new List<int> { page }, cancellationToken);

        var thumbPath = Path.Combine(folderPath, $"thumbnail.{page}.jpg");
        if (File.Exists(thumbPath))
        {
            return thumbPath;
        }

        return null;
    }

    public async Task<IEnumerable<object>?> GetThumbnailRangeAsync(string id, int start, int end, string baseUrl, CancellationToken cancellationToken)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath))
            return null;

        var pagesToCheck = Enumerable.Range(start, end - start + 1).ToList();
        var results = new List<object>();

        await EnsureThumbnailsExistAsync(folderPath, pagesToCheck, cancellationToken);

        foreach (var page in pagesToCheck)
        {
            var thumbPath = Path.Combine(folderPath, $"thumbnail.{page}.jpg");
            if (File.Exists(thumbPath))
            {
                results.Add(new { page, url = $"{baseUrl}/uploads/{id}/thumbnail.{page}.jpg" });
            }
        }

        return results;
    }

    private async Task EnsureThumbnailsExistAsync(string folderPath, List<int> pagesToCheck, CancellationToken cancellationToken)
    {
        var missingPages = pagesToCheck
            .Where(p => !File.Exists(Path.Combine(folderPath, $"thumbnail.{p}.jpg")))
            .ToList();

        if (missingPages.Count == 0) return;

        var pdfFile = new DirectoryInfo(folderPath)
            .GetFiles("*.pdf")
            .FirstOrDefault(f => !IsTemporaryPdf(f));

        if (pdfFile == null)
            throw new FileNotFoundException("PDF not found.");

        await PdfThumbnailService.GenerateThumbnailsPdfiumViewerParallelAsync(
            pdfFile.FullName,
            folderPath,
            thumbWidth: 300,
            thumbHeight: 300,
            dpi: 150,
            progress: null,
            cancellationToken: cancellationToken,
            pageList: missingPages
        );
    }

    public async Task<string?> GetThumbnailRangeZipAsync(string id, int start, int end)
    {
        if (start < 1 || end < start)
            throw new ArgumentException("Invalid page range.");

        string thumbFolder = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(thumbFolder))
            return null;

        string cacheFolder = Path.Combine(Directory.GetCurrentDirectory(), "cache", id);
        Directory.CreateDirectory(cacheFolder);

        string zipName = $"thumbnails_{start}_{end}.zip";
        string zipPath = Path.Combine(cacheFolder, zipName);

        if (File.Exists(zipPath))
        {
            return zipPath;
        }

        using (var zipStream = File.Create(zipPath))
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            var manifest = new List<object>();

            for (int page = start; page <= end; page++)
            {
                string filePath = Path.Combine(thumbFolder, $"thumbnail.{page}.jpg");
                if (!File.Exists(filePath))
                    continue; // skip missing thumbnails

                var entry = zip.CreateEntry($"thumbnail.{page}.jpg", CompressionLevel.Fastest);

                using (var entryStream = entry.Open())
                using (var fileStream = File.OpenRead(filePath))
                {
                    await fileStream.CopyToAsync(entryStream);
                }

                manifest.Add(new
                {
                    page,
                    file = $"thumbnail.{page}.jpg",
                    size = new FileInfo(filePath).Length
                });
            }

            // Add manifest.json
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Fastest);
            using (var manifestStream = manifestEntry.Open())
            using (var writer = new StreamWriter(manifestStream))
            {
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await writer.WriteAsync(json);
            }
        }

        return zipPath;
    }
}