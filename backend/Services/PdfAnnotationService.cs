using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PDFBackend.Models;
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
}