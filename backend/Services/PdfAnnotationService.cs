using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PDFBackend.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;

namespace PDFBackend.Services;

public class PdfAnnotationService : IPdfAnnotationService
{
    private readonly string _uploadFolder;

    public PdfAnnotationService()
    {
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    }

    private static bool IsTemporaryPdf(FileInfo file) =>
        file.Name.EndsWith(".tmp.pdf", StringComparison.OrdinalIgnoreCase) ||
        file.Name.EndsWith(".qpdf.pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> SaveAnnotationsAsync(string id, List<AnnotationBase> annotations)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) return false;

        var metadataPath = Path.Combine(folderPath, "pages-metadata.json");
        if (!File.Exists(metadataPath)) return false;

        var json = await File.ReadAllTextAsync(metadataPath);
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true, 
            IncludeFields = true 
        };
        
        var pageMetadataList = JsonSerializer.Deserialize<List<PageMetadata>>(json, options) 
                               ?? new List<PageMetadata>();

        var annotationsByPage = annotations.GroupBy(a => a.Page).ToDictionary(g => g.Key, g => g.ToArray());

        foreach (var pageMeta in pageMetadataList)
        {
            annotationsByPage.TryGetValue(pageMeta.PageNumber, out var pageAnnotations);
            pageMeta.Annotations = pageAnnotations; // Automatically clears annotations if none exist for this page
        }

        var updatedJson = JsonSerializer.Serialize(pageMetadataList, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        await File.WriteAllTextAsync(metadataPath, updatedJson);

        try
        {
            var pdfFile = new DirectoryInfo(folderPath).GetFiles("*.pdf").FirstOrDefault(f => !IsTemporaryPdf(f));
            if (pdfFile != null)
            {
                UpdateThumbnails(folderPath, pdfFile.FullName, pageMetadataList);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to update thumbnails: {ex.Message}");
        }

        return true;
    }

    public async Task<bool> ApplyAnnotationsAsync(string id)
    {
        var folderPath = Path.Combine(_uploadFolder, id);
        if (!Directory.Exists(folderPath)) throw new FileNotFoundException("File not found.");

        var pdfFile = new DirectoryInfo(folderPath).GetFiles("*.pdf").FirstOrDefault(f => !IsTemporaryPdf(f));
        if (pdfFile == null) throw new FileNotFoundException("PDF not found.");

        var metadataPath = Path.Combine(folderPath, "pages-metadata.json");
        if (!File.Exists(metadataPath)) throw new FileNotFoundException("Metadata not found.");

        var json = await File.ReadAllTextAsync(metadataPath);
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true, 
            IncludeFields = true 
        };
        
        var pageMetadataList = JsonSerializer.Deserialize<List<PageMetadata>>(json, options) 
                               ?? new List<PageMetadata>();

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
                            if (ann is AnnotationRectangle rect)
                            {
                                var pen = new XPen(ParseColor(rect.Color), 2 * scale);
                                gfx.DrawRectangle(pen, rect.Position.X * scale, rect.Position.Y * scale, rect.Width * scale, rect.Height * scale);
                            }
                            else if (ann is AnnotationFreehand freehand)
                            {
                                if (freehand.Points != null && freehand.Points.Count > 1)
                                {
                                    var pen = new XPen(ParseColor(freehand.Color), freehand.StrokeWidth * scale);
                                    var points = freehand.Points.Select(p => new XPoint(p.X * scale, p.Y * scale)).ToArray();
                                    gfx.DrawLines(pen, points);
                                }
                            }
                            else if (ann is AnnotationArrow arrow)
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
            await File.WriteAllTextAsync(metadataPath, updatedJson);
        }

        return pdfModified;
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

    private void UpdateThumbnails(string folderPath, string pdfPath, List<PageMetadata> pageMetadataList)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.InformationOnly);

        foreach (var meta in pageMetadataList)
        {
            var basePath = Path.Combine(folderPath, $"thumbnail.{meta.PageNumber}.base.jpg");
            var thumbPath = Path.Combine(folderPath, $"thumbnail.{meta.PageNumber}.jpg");

            // Backup original thumbnail on first edit
            if (!File.Exists(basePath) && File.Exists(thumbPath))
            {
                File.Copy(thumbPath, basePath);
            }

            if (!File.Exists(basePath)) continue;

            if (meta.Annotations == null || !meta.Annotations.Any())
            {
                // Restore original if no annotations exist
                File.Copy(basePath, thumbPath, true);
                continue;
            }

            // Draw annotations on the base thumbnail
            using var baseImg = Image.FromFile(basePath);
            using var bmp = new Bitmap(baseImg);
            using var gfx = Graphics.FromImage(bmp);
            
            gfx.SmoothingMode = SmoothingMode.AntiAlias;

            var page = document.Pages[meta.PageNumber - 1];
            double pdfWidth = page.Width.Point;
            double pdfHeight = page.Height.Point;
            
            // Frontend scales the PDF by 1.5x, matching scale in ApplyAnnotationsAsync
            double frontendWidth = pdfWidth * 1.5;
            double frontendHeight = pdfHeight * 1.5;
            
            double scaleX = bmp.Width / frontendWidth;
            double scaleY = bmp.Height / frontendHeight;

            foreach (var ann in meta.Annotations)
            {
                try 
                {
                    if (ann is AnnotationRectangle rect)
                    {
                        using var pen = new Pen(ParseDrawingColor(rect.Color), (float)(2 * scaleX));
                        gfx.DrawRectangle(pen, (float)(rect.Position.X * scaleX), (float)(rect.Position.Y * scaleY), (float)(rect.Width * scaleX), (float)(rect.Height * scaleY));
                    }
                    else if (ann is AnnotationFreehand freehand && freehand.Points != null && freehand.Points.Count > 1)
                    {
                        using var pen = new Pen(ParseDrawingColor(freehand.Color), (float)(freehand.StrokeWidth * scaleX));
                        var points = freehand.Points.Select(p => new PointF((float)(p.X * scaleX), (float)(p.Y * scaleY))).ToArray();
                        gfx.DrawLines(pen, points);
                    }
                    else if (ann is AnnotationArrow arrow)
                    {
                        using var pen = new Pen(ParseDrawingColor(arrow.Color), (float)(arrow.StrokeWidth * scaleX));
                        float x1 = (float)(arrow.Position.X * scaleX);
                        float y1 = (float)(arrow.Position.Y * scaleY);
                        float x2 = (float)(arrow.EndPosition.X * scaleX);
                        float y2 = (float)(arrow.EndPosition.Y * scaleY);
                        gfx.DrawLine(pen, x1, y1, x2, y2);

                        float headLength = (float)(10 * scaleX);
                        double dx = x2 - x1;
                        double dy = y2 - y1;
                        double angle = Math.Atan2(dy, dx);
                        gfx.DrawLine(pen, x2, y2, x2 - (float)(headLength * Math.Cos(angle - Math.PI / 6)), y2 - (float)(headLength * Math.Sin(angle - Math.PI / 6)));
                        gfx.DrawLine(pen, x2, y2, x2 - (float)(headLength * Math.Cos(angle + Math.PI / 6)), y2 - (float)(headLength * Math.Sin(angle + Math.PI / 6)));
                    }
                }
                catch { /* Ignore invalid annotation drawing */ }
            }

            bmp.Save(thumbPath, System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        // Invalidate zip cache if it exists, so next frontend request gets the updated thumbnails
        var zipPath = Path.Combine(folderPath, "thumbnails.zip");
        zipPath = zipPath.Replace("uploads", "cache"); // Assuming zip cache is stored in a parallel "cache" directory
        if (File.Exists(zipPath)) File.Delete(zipPath);
    }

    private System.Drawing.Color ParseDrawingColor(string colorHex, double opacity = 1.0)
    {
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(colorHex);
            return System.Drawing.Color.FromArgb((int)(opacity * 255), c.R, c.G, c.B);
        }
        catch
        {
            return System.Drawing.Color.FromArgb((int)(opacity * 255), 255, 0, 0); // Fallback to red
        }
    }
}