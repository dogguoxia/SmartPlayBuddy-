using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartPlayBuddy.Client.Models;

namespace SmartPlayBuddy.Client.Services;

public sealed class WebSocketService : IDisposable
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Message>? MessageReceived;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string url)
    {
        if (_socket is not null)
        {
            await DisconnectAsync();
        }

        _cts = new CancellationTokenSource();
        _socket = new ClientWebSocket();

        try
        {
            var uri = new Uri(url);
            await _socket.ConnectAsync(uri, _cts.Token);
            Connected?.Invoke(this, EventArgs.Empty);
            _ = ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    public async Task SendAsync(Message message)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var json = message.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cts?.Token ?? default);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_socket is not null && _socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
        }

        _socket?.Dispose();
        _socket = null;
        _cts?.Dispose();
        _cts = null;

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReceiveLoopAsync()
    {
        if (_socket is null) return;

        var buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (_socket.State == WebSocketState.Open && _cts is { IsCancellationRequested: false })
            {
                WebSocketReceiveResult result;
                sb.Clear();

                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                try
                {
                    var msg = Message.FromJson(sb.ToString());
                    MessageReceived?.Invoke(this, msg);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on disconnect
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
        _sendLock.Dispose();
    }
}
