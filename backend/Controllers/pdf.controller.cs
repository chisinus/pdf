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

    [HttpGet("pagesmetadata/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetPagesMetadata(string id)
    {
        var metadataPath = Path.Combine(_uploadFolder, id, "pages-metadata.json");
        if (!System.IO.File.Exists(metadataPath)) return NotFound(new { Message = "Metadata not found or still processing." });

        var json = System.IO.File.ReadAllText(metadataPath);
        return Content(json, "application/json");
    }

    [HttpPost("annotations/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAnnotations(string id, [FromBody] List<PDFBackend.Models.AnnotationBase> annotations)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) return NotFound(new { Message = "File not found." });

        var metadataPath = Path.Combine(folderPath, "pages-metadata.json");
        if (!System.IO.File.Exists(metadataPath)) return NotFound(new { Message = "Metadata not found." });

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(metadataPath);
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true, 
                IncludeFields = true // Important: PageMetadata uses fields instead of properties
            };
            
            var pageMetadataList = JsonSerializer.Deserialize<List<PDFBackend.Models.PageMetadata>>(json, options) 
                                   ?? new List<PDFBackend.Models.PageMetadata>();

            var annotationsByPage = annotations.GroupBy(a => a.Page).ToDictionary(g => g.Key, g => g.ToArray());

            foreach (var pageMeta in pageMetadataList)
            {
                annotationsByPage.TryGetValue(pageMeta.PageNumber, out var pageAnnotations);
                pageMeta.Annotations = pageAnnotations; // Automatically clears annotations if none exist for this page
            }

            var updatedJson = JsonSerializer.Serialize(pageMetadataList, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            await System.IO.File.WriteAllTextAsync(metadataPath, updatedJson);

            return Ok(new { Message = "Annotations saved successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error saving annotations.", Detail = ex.Message });
        }
    }

    [HttpGet("thumbnails/{id}/{page}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetThumbnail(string id, int page)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath))
            return NotFound(new { Message = "File not found." });

        try
        {
            await EnsureThumbnailsExistAsync(folderPath, new List<int> { page });

            var thumbPath = Path.Combine(folderPath, $"thumbnail.{page}.jpg");
            if (System.IO.File.Exists(thumbPath))
            {
                return PhysicalFile(thumbPath, "image/jpeg");
            }
            
            return NotFound(new { Message = "Thumbnail not found after generation attempt." });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { Message = "Request cancelled." });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error generating thumbnail.", Detail = ex.Message });
        }
    }

    [HttpGet("thumbnails/range/{id}/{start}/{end}")]
    public async Task<IActionResult> GetThumbnailRange(string id, int start, int end)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var results = new List<object>();
        var folderPath = Path.Combine(_uploadFolder, id);

        if (!Directory.Exists(folderPath))
            return NotFound(new { Message = "File not found." });

        var pagesToCheck = Enumerable.Range(start, end - start + 1).ToList();

        try
        {
            await EnsureThumbnailsExistAsync(folderPath, pagesToCheck);

            foreach (var page in pagesToCheck)
            {
                var thumbPath = Path.Combine(folderPath, $"thumbnail.{page}.jpg");
                if (System.IO.File.Exists(thumbPath))
                {
                    results.Add(new { page, url = $"{baseUrl}/uploads/{id}/thumbnail.{page}.jpg" });
                }
            }
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { Message = "Request cancelled." });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error generating thumbnails.", Detail = ex.Message });
        }

        return Ok(new
        {
            id,
            start,
            end,
            thumbnails = results
        });
    }

    private async Task EnsureThumbnailsExistAsync(string folderPath, List<int> pagesToCheck)
    {
        var missingPages = pagesToCheck
            .Where(p => !System.IO.File.Exists(Path.Combine(folderPath, $"thumbnail.{p}.jpg")))
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
            cancellationToken: HttpContext.RequestAborted,
            pageList: missingPages
        );
    }
}