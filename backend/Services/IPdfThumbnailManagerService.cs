using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PDFBackend.Services;

public interface IPdfThumbnailManagerService
{
    Task<string?> GetThumbnailAsync(string id, int page, CancellationToken cancellationToken);
    Task<IEnumerable<object>?> GetThumbnailRangeAsync(string id, int start, int end, string baseUrl, CancellationToken cancellationToken);
    Task<string?> GetThumbnailRangeZipAsync(string id, int start, int end);
}