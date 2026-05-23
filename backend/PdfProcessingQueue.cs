using System.Threading.Channels;

namespace PDFBackend.Services;

public class PdfProcessingQueue
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    public async ValueTask QueueBackgroundWorkItemAsync(string directoryPath)
    {
        await _queue.Writer.WriteAsync(directoryPath);
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}