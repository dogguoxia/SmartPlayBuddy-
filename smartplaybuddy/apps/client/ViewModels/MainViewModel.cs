using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using SmartPlayBuddy.Client.Drivers;
using SmartPlayBuddy.Client.Models;
using SmartPlayBuddy.Client.Services;
using Windows.Storage;

namespace SmartPlayBuddy.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public string[] Modes { get; } = { "text", "screenshot" };

    private readonly WebSocketService _webSocket = new();
    private readonly DriverRegistry _registry = new();
    private readonly DispatcherQueue _dispatcher;

    private string _wsUrl = "ws://localhost:2508/ws";
    private string _connectionStatus = "未连接";
    private string _selectedMode = "text";
    private string _textMessage = "";
    private string? _deviceInfo;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        LoadSettings();

        _webSocket.Connected += OnConnected;
        _webSocket.Disconnected += OnDisconnected;
        _webSocket.MessageReceived += OnMessageReceived;
        _webSocket.ErrorOccurred += OnErrorOccurred;

        if (_registry.GetScreenDriver() is { } screenDriver)
        {
            screenDriver.FrameCaptured += OnFrameCaptured;
        }

        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected);
        SendCommand = new RelayCommand(async () => await SendAsync(), () => IsConnected);
    }

    private void UpdateModeDefaults()
    {
        OnPropertyChanged(nameof(IsTextMode));
        OnPropertyChanged(nameof(IsScreenshotMode));
    }

    public string WsUrl
    {
        get => _wsUrl;
        set => SetProperty(ref _wsUrl, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsConnected => _webSocket.IsConnected;

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                UpdateModeDefaults();
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTextMode => _selectedMode == "text";
    public bool IsScreenshotMode => _selectedMode == "screenshot";

    public string TextMessage
    {
        get => _textMessage;
        set => SetProperty(ref _textMessage, value);
    }

    public string? DeviceInfo
    {
        get => _deviceInfo;
        private set => SetProperty(ref _deviceInfo, value);
    }

    public ObservableCollection<string> Logs { get; } = new();

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SendCommand { get; }

    public async Task ConnectAsync()
    {
        try
        {
            ConnectionStatus = "连接中...";
            await _webSocket.ConnectAsync(WsUrl);
            SaveSettings();

            var claim = new Message
            {
                Type = "session",
                Action = "claim",
                Data = new
                {
                    device = new
                    {
                        type = "client",
                        deviceName = Environment.MachineName,
                        deviceInfo = "",
                        platform = "win32",
                        machine = Environment.Is64BitOperatingSystem ? "AMD64" : "x86",
                        appVersion = "0.0.1",
                        screenResolution = "unknown",
                    }
                }
            };
            await _webSocket.SendAsync(claim);
        }
        catch (Exception ex)
        {
            Log($"连接失败: {ex.Message}");
            ConnectionStatus = "未连接";
        }
    }

    public async Task DisconnectAsync()
    {
        await _webSocket.DisconnectAsync();
    }

    public async Task SendAsync()
    {
        if (_selectedMode == "screenshot")
        {
            await SendScreenshotAsync();
        }
        else
        {
            await SendTextAsync();
        }
    }

    public async Task SendTextAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TextMessage))
            {
                Log("文字内容为空");
                return;
            }

            var msg = new Message
            {
                Type = "chat",
                Action = "text",
                Data = TextMessage.Trim(),
            };
            await _webSocket.SendAsync(msg);
            Log($"发送文字: {TextMessage.Trim()}");
        }
        catch (Exception ex)
        {
            Log($"发送失败: {ex.Message}");
        }
    }

    public async Task SendScreenshotAsync()
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["operate"] = "capture",
                ["format"] = "png",
            };

            var result = await _registry.ExecuteAsync("screen", parameters);
            if (result.Status != "ok" || result.Data is null)
            {
                Log($"截图失败: {result.Message}");
                return;
            }

            var response = new Message
            {
                Type = "stream",
                Action = "screen",
                Data = BuildResponseData(result, new { mode = "manual_screenshot" }),
            };

            await _webSocket.SendAsync(response);
            Log($"发送截图: {result.Data.Length} bytes");
        }
        catch (Exception ex)
        {
            Log($"截图发送失败: {ex.Message}");
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            ConnectionStatus = "已连接";
            DeviceInfo = null;
            Log("已连接到服务端");
            RaiseCanExecuteChanged();
        });
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _registry.StopAll();
        _dispatcher.TryEnqueue(() =>
        {
            ConnectionStatus = "未连接";
            DeviceInfo = null;
            Log("已断开连接");
            RaiseCanExecuteChanged();
        });
    }

    private void OnMessageReceived(object? sender, Message msg)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var preview = msg.Data is JsonElement element
                ? JsonSerializer.Serialize(element).Substring(0, Math.Min(120, JsonSerializer.Serialize(element).Length))
                : JsonSerializer.Serialize(msg.Data);
            Log($"[{msg.Type}/{msg.Action}] {preview}");

            if (msg.Type == "session" && msg.Action == "claimed")
            {
                DeviceInfo = $"设备 ID: {msg.Data}";
            }
            else if (msg.Type == "system" && msg.Action == "ping")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _webSocket.SendAsync(new Message
                        {
                            Type = "system",
                            Action = "pong",
                            Data = msg.Data,
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"pong 失败: {ex.Message}");
                    }
                });
            }
            else if (msg.Type == "command")
            {
                _ = HandleCommandAsync(msg);
            }
        });
    }

    private async Task HandleCommandAsync(Message msg)
    {
        try
        {
            if (msg.Action == "chat" && msg.Data is string chatText)
            {
                Log($"[收到文字] {chatText}");
                await _webSocket.SendAsync(new Message
                {
                    Type = "response",
                    Action = "chat",
                    To = msg.From,
                    RequestId = msg.RequestId,
                    Data = new { received = true },
                });
                return;
            }

            var parameters = ToDictionary(msg.Data);
            var result = await _registry.ExecuteAsync(msg.Action, parameters);

            if (result.Status == "ok")
            {
                var response = new Message
                {
                    Type = "response",
                    Action = msg.Action,
                    To = msg.From,
                    RequestId = msg.RequestId,
                    Data = BuildResponseData(result),
                };
                await _webSocket.SendAsync(response);
                Log($"驱动 [{msg.Action}] 执行成功");
            }
            else
            {
                var error = new Message
                {
                    Type = "error",
                    Action = msg.Action,
                    To = msg.From,
                    RequestId = msg.RequestId,
                    Data = result.Message ?? "driver error",
                };
                await _webSocket.SendAsync(error);
                Log($"驱动 [{msg.Action}] 执行失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"处理指令失败: {ex.Message}");
            try
            {
                await _webSocket.SendAsync(new Message
                {
                    Type = "error",
                    Action = msg.Action,
                    To = msg.From,
                    RequestId = msg.RequestId,
                    Data = ex.Message,
                });
            }
            catch { }
        }
    }

    private void OnFrameCaptured(object? sender, (string StreamId, DriverResult Result) e)
    {
        _dispatcher.TryEnqueue(() =>
        {
            var stream = new Message
            {
                Type = "stream",
                Action = "screen",
                Data = BuildResponseData(e.Result, new { stream_id = e.StreamId }),
            };
            _ = _webSocket.SendAsync(stream);
        });
    }

    private static object BuildResponseData(DriverResult result, object? extra = null)
    {
        var dict = new Dictionary<string, object?>
        {
            ["status"] = result.Status,
            ["result"] = result.Result,
        };

        if (extra is not null)
        {
            dict["extra"] = extra;
        }

        if (result.Data is not null)
        {
            dict["__data__"] = Convert.ToBase64String(result.Data);
            dict["__mime__"] = result.Mime;
        }

        return dict;
    }

    private static Dictionary<string, object?> ToDictionary(object? data)
    {
        if (data is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>();
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = JsonElementToObject(property.Value);
            }
            return result;
        }

        return new Dictionary<string, object?>();
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone(),
        };
    }

    private void OnErrorOccurred(object? sender, Exception ex)
    {
        _dispatcher.TryEnqueue(() => Log($"错误: {ex.Message}"));
    }

    private void Log(string message)
    {
        _dispatcher.TryEnqueue(() =>
        {
            Logs.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
            if (Logs.Count > 500)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void LoadSettings()
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue("WsUrl", out var value) && value is string s)
        {
            WsUrl = s;
        }
    }

    private void SaveSettings()
    {
        ApplicationData.Current.LocalSettings.Values["WsUrl"] = WsUrl;
    }

    private void RaiseCanExecuteChanged()
    {
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Func<Task> execute, Func<bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute();

        public async void Execute(object? parameter) => await _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
