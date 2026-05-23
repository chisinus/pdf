namespace PDFBackend.Models;

public class FileInfo
{
    public required string Name { get; set; }
    public required string PDFName { get; set; }
    public required string Url { get; set; }
    public long Size { get; set; }
    public int Thumbnails { get; set; }
}