using Microsoft.AspNetCore.Mvc;
using PDFBackend.Services;
using System.Runtime;
using System.Text.Json;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using System.IO.Compression;

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

    [HttpPost("annotations/apply/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyAnnotations(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) return NotFound(new { Message = "File not found." });

        var pdfFile = new DirectoryInfo(folderPath).GetFiles("*.pdf").FirstOrDefault(f => !IsTemporaryPdf(f));
        if (pdfFile == null) return NotFound(new { Message = "PDF not found." });

        var metadataPath = Path.Combine(folderPath, "pages-metadata.json");
        if (!System.IO.File.Exists(metadataPath)) return NotFound(new { Message = "Metadata not found." });

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(metadataPath);
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true, 
                IncludeFields = true 
            };
            
            var pageMetadataList = JsonSerializer.Deserialize<List<PDFBackend.Models.PageMetadata>>(json, options) 
                                   ?? new List<PDFBackend.Models.PageMetadata>();

            bool pdfModified = false;
            double scale = 1.0 / 1.5; // Frontend drawn at 1.5x scale viewport, converting back to native PDF points

            using (var document = PdfReader.Open(pdfFile.FullName, PdfDocumentOpenMode.Modify))
            {
                foreach (var meta in pageMetadataList)
                {
                    if (meta.Annotations != null && meta.Annotations.Any())
                    {
                        var page = document.Pages[meta.PageNumber - 1];
                        using (var gfx = XGraphics.FromPdfPage(page))
                        {
                            foreach (var ann in meta.Annotations)
                            {
                                if (ann is PDFBackend.Models.AnnotationRectangle rect)
                                {
                                    var pen = new XPen(ParseColor(rect.Color), 2 * scale);
                                    gfx.DrawRectangle(pen, rect.Position.X * scale, rect.Position.Y * scale, rect.Width * scale, rect.Height * scale);
                                }
                                //else if (ann is PDFBackend.Models.AnnotationHighlight highlight)
                                //{
                                //    var brush = new XSolidBrush(ParseColor(highlight.Color, highlight.Opacity));
                                //    gfx.DrawRectangle(brush, highlight.Position.X * scale, highlight.Position.Y * scale, highlight.Width * scale, highlight.Height * scale);
                                //}
                                else if (ann is PDFBackend.Models.AnnotationFreehand freehand)
                                {
                                    if (freehand.Points != null && freehand.Points.Count > 1)
                                    {
                                        var pen = new XPen(ParseColor(freehand.Color), freehand.StrokeWidth * scale);
                                        var points = freehand.Points.Select(p => new XPoint(p.X * scale, p.Y * scale)).ToArray();
                                        gfx.DrawLines(pen, points);
                                    }
                                }
                                else if (ann is PDFBackend.Models.AnnotationArrow arrow)
                                {
                                    var pen = new XPen(ParseColor(arrow.Color), arrow.StrokeWidth * scale);
                                double x1 = arrow.Position.X * scale;
                                double y1 = arrow.Position.Y * scale;
                                double x2 = arrow.EndPosition.X * scale;
                                double y2 = arrow.EndPosition.Y * scale;
                                gfx.DrawLine(pen, x1, y1, x2, y2);

                                double headLength = 10 * scale;
                                double dx = x2 - x1;
                                double dy = y2 - y1;
                                double angle = Math.Atan2(dy, dx);
                                gfx.DrawLine(pen, x2, y2, x2 - headLength * Math.Cos(angle - Math.PI / 6), y2 - headLength * Math.Sin(angle - Math.PI / 6));
                                gfx.DrawLine(pen, x2, y2, x2 - headLength * Math.Cos(angle + Math.PI / 6), y2 - headLength * Math.Sin(angle + Math.PI / 6));
                                }
                            }
                        }
                        meta.Annotations = null; // Clear annotations from metadata
                        pdfModified = true;
                    }
                }

                if (pdfModified)
                {
                    document.Save(pdfFile.FullName);
                }
            }

            if (pdfModified)
            {
                var updatedJson = JsonSerializer.Serialize(pageMetadataList, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                await System.IO.File.WriteAllTextAsync(metadataPath, updatedJson);
            }

            return Ok(new { Message = "Annotations applied to PDF and removed from metadata." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error applying annotations.", Detail = ex.Message });
        }
    }

    private XColor ParseColor(string colorHex, double opacity = 1.0)
    {
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(colorHex);
            return XColor.FromArgb((int)(opacity * 255), c.R, c.G, c.B);
        }
        catch
        {
            return XColor.FromArgb((int)(opacity * 255), 255, 0, 0); // Fallback to red
        }
    }

    [HttpGet("pdf/thumbnails/rangezip/{id}/{start}/{end}")]
    public async Task<IActionResult> GetThumbnailRangeZip(
        string id,
        int start,
        int end)
    {
        if (start < 1 || end < start)
            return BadRequest("Invalid page range.");

        string thumbFolder = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(thumbFolder))
            return NotFound("Thumbnail folder not found.");

        string cacheFolder = Path.Combine(Directory.GetCurrentDirectory(), "cache", id);
        Directory.CreateDirectory(cacheFolder);

        string zipName = $"thumbnails_{start}_{end}.zip";
        string zipPath = Path.Combine(cacheFolder, zipName);

        // If cached ZIP exists, stream it immediately
        if (System.IO.File.Exists(zipPath))
        {
            return PhysicalFile(zipPath, "application/zip", zipName);
        }

        // Otherwise, create the ZIP and cache it
        using (var zipStream = System.IO.File.Create(zipPath))
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            var manifest = new List<object>();

            for (int page = start; page <= end; page++)
            {
                string filePath = Path.Combine(thumbFolder, $"thumbnail.{page}.jpg");
                if (!System.IO.File.Exists(filePath))
                    continue; // skip missing thumbnails

                var entry = zip.CreateEntry($"thumbnail.{page}.jpg", CompressionLevel.Fastest);

                using (var entryStream = entry.Open())
                using (var fileStream = System.IO.File.OpenRead(filePath))
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

        // Now stream the cached ZIP
        return PhysicalFile(zipPath, "application/zip", zipName);
    }
}