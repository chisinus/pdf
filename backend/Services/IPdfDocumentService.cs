using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PDFBackend.Services;

public interface IPdfDocumentService
{
    Task<string> UploadAsync(IFormFile file);
    IEnumerable<object> GetFiles(string baseUrl);
    bool Delete(string id);
    string? GetPdfFilePath(string id);
    Task<string?> GetPagesMetadataAsync(string id);
}