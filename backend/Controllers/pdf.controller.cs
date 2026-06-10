using Microsoft.AspNetCore.Mvc;
using PDFBackend.Services;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;

namespace PDFBackend.Controllers;

[ApiController]
[Route("api")]
public class PdfController : ControllerBase
{
    private const long MaxUploadBytes = 500 * 1024 * 1024; // 500 MB

    private readonly IPdfDocumentService _documentService;
    private readonly IPdfAnnotationService _annotationService;
    private readonly IPdfThumbnailManagerService _thumbnailManagerService;

    public PdfController(
        IPdfDocumentService documentService,
        IPdfAnnotationService annotationService,
        IPdfThumbnailManagerService thumbnailManagerService)
    {
        _documentService = documentService;
        _annotationService = annotationService;
        _thumbnailManagerService = thumbnailManagerService;
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

        var folderName = await _documentService.UploadAsync(file);
        return Accepted(new { Message = "File uploaded and processing has started.", Id = folderName });
    }

    [HttpGet]
    [Route("files")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFiles()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var files = _documentService.GetFiles(baseUrl);
        return Ok(files);
    }

    [HttpDelete]
    [Route("delete/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(string id)
    {
        if (_documentService.Delete(id))
            return Ok(new { Message = "File deleted successfully." });
            
        return NotFound(new { Message = "File not found." });
    }

    [HttpGet("download/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Download(string id)
    {
        var filePath = _documentService.GetPdfFilePath(id);
        if (filePath == null) return NotFound();
        
        return PhysicalFile(filePath, "application/pdf", enableRangeProcessing: true);
    }

    [HttpGet("pagesmetadata/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPagesMetadata(string id)
    {
        var json = await _documentService.GetPagesMetadataAsync(id);
        if (json == null) return NotFound(new { Message = "Metadata not found or still processing." });

        return Content(json, "application/json");
    }

    [HttpPost("annotations/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAnnotations(string id, [FromBody] List<PDFBackend.Models.AnnotationBase> annotations)
    {
        try
        {
            var success = await _annotationService.SaveAnnotationsAsync(id, annotations);
            if (!success) return NotFound(new { Message = "File or Metadata not found." });

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
        try
        {
            var thumbPath = await _thumbnailManagerService.GetThumbnailAsync(id, page, HttpContext.RequestAborted);
            if (thumbPath != null)
                return PhysicalFile(thumbPath, "image/jpeg");
            
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
        try
        {
            var thumbnails = await _thumbnailManagerService.GetThumbnailRangeAsync(id, start, end, baseUrl, HttpContext.RequestAborted);
            if (thumbnails == null)
                return NotFound(new { Message = "File not found." });

            return Ok(new
            {
                id,
                start,
                end,
                thumbnails
            });
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
    }

    [HttpPost("annotations/apply/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyAnnotations(string id)
    {
        try
        {
            await _annotationService.ApplyAnnotationsAsync(id);
            return Ok(new { Message = "Annotations applied to PDF and removed from metadata." });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error applying annotations.", Detail = ex.Message });
        }
    }

    [HttpGet("pdf/thumbnails/rangezip/{id}/{start}/{end}")]
    public async Task<IActionResult> GetThumbnailRangeZip(string id, int start, int end)
    {
        try
        {
            var zipPath = await _thumbnailManagerService.GetThumbnailRangeZipAsync(id, start, end);
            if (zipPath == null)
                return NotFound("Thumbnail folder not found.");

            string zipName = $"thumbnails_{start}_{end}.zip";
            return PhysicalFile(zipPath, "application/zip", zipName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Error creating zip.", Detail = ex.Message });
        }
    }
}