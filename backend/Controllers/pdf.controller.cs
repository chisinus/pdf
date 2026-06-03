using Microsoft.AspNetCore.Mvc;
using PDFBackend.Services;
using System.Runtime;
using System.Text.Json;
using FileInfo = PDFBackend.Models.FileInfo;

namespace PDFBackend.Controllers;

[ApiController]
//[Route("api/[controller]")]
[Route("api")]
public class PdfController : ControllerBase
{
    private const long MaxUploadBytes = 500 * 1024 * 1024; // 500 MB
    private readonly string _uploadFolder;
    private readonly PdfProcessingQueue _queue;

    private static bool IsTemporaryPdf(System.IO.FileInfo file) =>
        file.Name.EndsWith(".tmp.pdf", StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(".qpdf.pdf", StringComparison.OrdinalIgnoreCase);

    public PdfController(PdfProcessingQueue queue)
    {
        _queue = queue;
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        if (!Directory.Exists(_uploadFolder))
        {
            Directory.CreateDirectory(_uploadFolder);
        }
    }

    [HttpPost]
    [Route("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file was uploaded.");

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

        // Queue the background job and return 202 Accepted immediately
        await _queue.QueueBackgroundWorkItemAsync(folderPath);

        return Accepted(new { Message = "File uploaded and processing has started.", Id = folderName });
    }

    [HttpGet]
    [Route("files")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFiles()
    {
        var files = new List<object>();
        foreach (var dir in Directory.GetDirectories(_uploadFolder))
        {
            var dirInfo = new DirectoryInfo(dir);
            var pdfFile = dirInfo
                .GetFiles("*.pdf")
                .FirstOrDefault(f=>!IsTemporaryPdf(f));

            if (pdfFile != null)
            {
                int pageCount = 0;
                var metadataPath = Path.Combine(dir, "pages-metadata.json");
                if (System.IO.File.Exists(metadataPath))
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(metadataPath);
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
                    Url = $"http://localhost:4001/api/download/{dirInfo.Name}",
                    Size = pdfFile.Length,
                    Thumbnails = pageCount > 0 ? pageCount : dirInfo.GetFiles("thumbnail.*.jpg").Length
                });
            }
        }
        return Ok(files);
    }

    [HttpDelete]
    [Route("delete/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
            return Ok(new { Message = "File deleted successfully." });
        }
        return NotFound(new { Message = "File not found." });
    }

    [HttpGet("download/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Download(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) return NotFound();

        var pdfFile = new DirectoryInfo(folderPath)
            .GetFiles("*.pdf")
            .FirstOrDefault(f => !IsTemporaryPdf(f));

        if (pdfFile == null) return NotFound();

        // PhysicalFile with enableRangeProcessing: true supports 206 Partial Content (Byte-Range requests)
        return PhysicalFile(pdfFile.FullName, "application/pdf", enableRangeProcessing: true);
    }

    [HttpGet("metadata/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetMetadata(string id)
    {
        var metadataPath = Path.Combine(_uploadFolder, id, "pages-metadata.json");
        if (!System.IO.File.Exists(metadataPath)) return NotFound(new { Message = "Metadata not found or still processing." });

        var json = System.IO.File.ReadAllText(metadataPath);
        return Content(json, "application/json");
    }

    [HttpGet("thumbnails/{id}/{page}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetThumbnail(string id, int page)
    {
        System.Console.WriteLine($"Requesting thumbnail for ID: {id}, Page: {page}");
        var thumbPath = Path.Combine(_uploadFolder, id, $"thumbnail.{page}.jpg");
        if (!System.IO.File.Exists(thumbPath)) return NotFound();

        var aa = PhysicalFile(thumbPath, "image/jpeg");

        return PhysicalFile(thumbPath, "image/jpeg");
    }

    [HttpGet("api/thumbnails/range/{id}/{start}/{end}")]
    public IActionResult GetThumbnailRange(string id, int start, int end)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var results = new List<object>();

        for (int page = start; page <= end; page++)
        {
            var thumbPath = Path.Combine(_uploadFolder, id, $"thumbnail.{page}.jpg");

            if (System.IO.File.Exists(thumbPath))
            {
                results.Add(new
                {
                    page,
                    url = $"{baseUrl}/api/thumbnails/{id}/{page}"
                });
            }
        }

        return Ok(new
        {
            id,
            start,
            end,
            thumbnails = results
        });
    }
}