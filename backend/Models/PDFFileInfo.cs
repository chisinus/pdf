namespace PDFBackend.Models;

public class PDFFileInfo
{
    public required string Name { get; set; }
    public required string PDFName { get; set; }
    public required string Url { get; set; }
    public long Size { get; set; }
    public int Thumbnails { get; set; }
}