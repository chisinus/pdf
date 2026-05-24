using Microsoft.AspNetCore.Mvc;
using PDFBackend.Services;
using FileInfo = PDFBackend.Models.FileInfo;

namespace PDFBackend.Controllers;

[ApiController]
//[Route("api/[controller]")]
[Route("api")]
public class PdfController : ControllerBase
{
    private readonly string _uploadFolder;
    private readonly PdfProcessingQueue _queue;

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
    [ProducesResponseType(typeof(IEnumerable<FileInfo>), StatusCodes.Status200OK)]
    public IActionResult GetFiles()
    {
        var files = new List<FileInfo>();
        foreach (var dir in Directory.GetDirectories(_uploadFolder))
        {
            var dirInfo = new DirectoryInfo(dir);
            var pdfFile = dirInfo.GetFiles("*.pdf").FirstOrDefault();
            if (pdfFile != null)
            {
                files.Add(new FileInfo
                {
                    Name = dirInfo.Name,
                    PDFName = pdfFile.Name,
                    Url = $"/api/pdf/{dirInfo.Name}/download",
                    Size = pdfFile.Length,
                    Thumbnails = dirInfo.GetFiles("thumbnail.*.png").Length
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

        var pdfFile = new DirectoryInfo(folderPath).GetFiles("*.pdf").FirstOrDefault();
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

    [HttpGet("/thumbnails/{page}/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetThumbnail(string id, int page)
    {
        var thumbPath = Path.Combine(_uploadFolder, id, $"thumbnail.{page}.png");
        if (!System.IO.File.Exists(thumbPath)) return NotFound();

        return PhysicalFile(thumbPath, "image/png");
    }
}