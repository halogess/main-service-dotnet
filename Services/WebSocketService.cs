using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ValidasiTugasAkhir.MainService.Services;

public interface IWebSocketService
{
    Task HandleWebSocketAsync(WebSocket webSocket, string nrp);
    Task NotifyDokumenStatusChanged(string nrp, int dokumenId, string status);
    Task NotifyDokumenCancelled(string nrp, int dokumenId);
    Task NotifyQueuePositionChanged(string nrp, int position);
    Task NotifyBukuStatusChanged(string nrp, int bukuId, string status);
    Task NotifyBukuArchiveReady(string nrp, int bukuId, bool docxReady, bool pdfReady);
    Task NotifyBukuCancelled(string nrp, int bukuId);
    Task NotifyValidationProgress(string nrp, int dokumenId, string stage, int progress);
}

public class WebSocketService : IWebSocketService
{
    private static readonly ConcurrentDictionary<string, List<WebSocket>> _connections = new();

    public async Task HandleWebSocketAsync(WebSocket webSocket, string nrp)
    {
        _connections.AddOrUpdate(nrp, 
            new List<WebSocket> { webSocket }, 
            (key, list) => { list.Add(webSocket); return list; });

        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        finally
        {
            if (_connections.TryGetValue(nrp, out var list))
            {
                list.Remove(webSocket);
                if (list.Count == 0) _connections.TryRemove(nrp, out var removed);
            }
        }
    }

    public async Task NotifyDokumenStatusChanged(string nrp, int dokumenId, string status)
    {
        await SendMessage(nrp, new
        {
            type = "status_changed",
            dokumen_id = dokumenId,
            status = status,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyDokumenCancelled(string nrp, int dokumenId)
    {
        await SendMessage(nrp, new
        {
            type = "dokumen_cancelled",
            dokumen_id = dokumenId,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyQueuePositionChanged(string nrp, int position)
    {
        await SendMessage(nrp, new
        {
            type = "queue_position_changed",
            position = position,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyBukuStatusChanged(string nrp, int bukuId, string status)
    {
        await SendMessage(nrp, new
        {
            type = "buku_status_changed",
            buku_id = bukuId,
            status = status,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyBukuArchiveReady(string nrp, int bukuId, bool docxReady, bool pdfReady)
    {
        await SendMessage(nrp, new
        {
            type = "buku_archive_ready",
            buku_id = bukuId,
            docx_ready = docxReady,
            pdf_ready = pdfReady,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyBukuCancelled(string nrp, int bukuId)
    {
        await SendMessage(nrp, new
        {
            type = "buku_cancelled",
            buku_id = bukuId,
            timestamp = DateTime.Now
        });
    }

    public async Task NotifyValidationProgress(string nrp, int dokumenId, string stage, int progress)
    {
        await SendMessage(nrp, new
        {
            type = "validation_progress",
            dokumen_id = dokumenId,
            stage = stage,
            progress = progress,
            timestamp = DateTime.Now
        });
    }

    private async Task SendMessage(string nrp, object message)
    {
        if (!_connections.TryGetValue(nrp, out var sockets)) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var tasks = sockets.Where(s => s.State == WebSocketState.Open)
            .Select(s => s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None));
        
        await Task.WhenAll(tasks);
    }
}
