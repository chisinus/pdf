using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PDFBackend.Services;

public class PdfDocumentService : IPdfDocumentService
{
    private readonly string _uploadFolder;
    private readonly PdfProcessingQueue _queue;

    public PdfDocumentService(PdfProcessingQueue queue)
    {
        _queue = queue;
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        if (!Directory.Exists(_uploadFolder))
        {
            Directory.CreateDirectory(_uploadFolder);
        }
    }

    private static bool IsTemporaryPdf(FileInfo file) =>
        file.Name.EndsWith(".tmp.pdf", StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(".qpdf.pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> UploadAsync(IFormFile file)
    {
        // Create a new subfolder with a GUID
        var folderName = Guid.NewGuid().ToString();
        var folderPath = Path.Combine(_uploadFolder, folderName);
        Directory.CreateDirectory(folderPath);

        // Save the file into the new folder
        var filePath = Path.Combine(folderPath, file.FileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Queue the background job and return folder name
        await _queue.QueueBackgroundWorkItemAsync(folderPath);

        return folderName;
    }

    public IEnumerable<object> GetFiles(string baseUrl)
    {
        var files = new List<object>();
        if (!Directory.Exists(_uploadFolder)) return files;

        foreach (var dir in Directory.GetDirectories(_uploadFolder))
        {
            var dirInfo = new DirectoryInfo(dir);
            var pdfFile = dirInfo
                .GetFiles("*.pdf")
                .FirstOrDefault(f => !IsTemporaryPdf(f));

            if (pdfFile != null)
            {
                int pageCount = 0;
                var metadataPath = Path.Combine(dir, "pages-metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(metadataPath);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("pageCount", out var pc)) pageCount = pc.GetInt32();
                        else if (doc.RootElement.TryGetProperty("PageCount", out var pc2)) pageCount = pc2.GetInt32();
                    }
                    catch { /* Ignore parsing errors */ }
                }

                files.Add(new
                {
                    UniqueName = dirInfo.Name,
                    Name = pdfFile.Name,
                    Url = $"{baseUrl}/api/download/{dirInfo.Name}",
                    Size = pdfFile.Length,
                    Thumbnails = pageCount > 0 ? pageCount : dirInfo.GetFiles("thumbnail.*.jpg").Length
                });
            }
        }
        return files;
    }

    public bool Delete(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
            return true;
        }
        return false;
    }

    public string? GetPdfFilePath(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) return null;

        var pdfFile = new DirectoryInfo(folderPath)
            .GetFiles("*.pdf")
            .FirstOrDefault(f => !IsTemporaryPdf(f));

        return pdfFile?.FullName;
    }

    public async Task<string?> GetPagesMetadataAsync(string id)
    {
        var metadataPath = Path.Combine(_uploadFolder, id, "pages-metadata.json");
        if (!File.Exists(metadataPath)) return null;

        return await File.ReadAllTextAsync(metadataPath);
    }
}