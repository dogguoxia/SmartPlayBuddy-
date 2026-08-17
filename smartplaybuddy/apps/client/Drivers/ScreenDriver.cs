using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SmartPlayBuddy.Client.Drivers;

public class ScreenDriver : IDriver, IDisposable
{
    public string Name => "screen";
    public string Description => "Screen capture driver (GDI)";

    private readonly Dictionary<string, CancellationTokenSource> _streams = new();
    private readonly object _lock = new();

    public event EventHandler<(string StreamId, DriverResult Result)>? FrameCaptured;

    public Task<DriverResult> OperateAsync(Dictionary<string, object?> parameters)
    {
        var op = GetString(parameters, "operate", "capture").ToLowerInvariant();

        return op switch
        {
            "capture" => Task.FromResult(Capture(parameters)),
            "list_monitors" => Task.FromResult(ListMonitors()),
            "start_stream" => Task.FromResult(StartStream(parameters)),
            "stop_stream" => Task.FromResult(StopStream(parameters)),
            _ => Task.FromResult(DriverResult.Error($"unknown operate: {op}")),
        };
    }

    private DriverResult Capture(Dictionary<string, object?> parameters)
    {
        var fmt = GetString(parameters, "fmt", GetString(parameters, "format", "png"));
        var quality = GetInt(parameters, "quality", 80);
        var scale = GetDouble(parameters, "scale", 1.0);
        var monitorIndex = GetInt(parameters, "monitor", 0);

        using var bitmap = CaptureMonitor(monitorIndex);
        if (bitmap is null)
        {
            return DriverResult.Error("failed to capture screen");
        }

        var width = bitmap.Width;
        var height = bitmap.Height;

        byte[] data;
        if (fmt.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            data = EncodeJpeg(bitmap, quality);
        }
        else
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            data = ms.ToArray();
        }

        return DriverResult.Ok(
            new { format = fmt, width, height },
            data,
            $"image/{fmt.ToLowerInvariant()}");
    }

    private static Bitmap? CaptureMonitor(int monitorIndex)
    {
        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds is null)
        {
            return null;
        }

        var bitmap = new Bitmap(bounds.Value.Width, bounds.Value.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(bounds.Value.Left, bounds.Value.Top, 0, 0, bounds.Value.Size);
        return bitmap;
    }

    private DriverResult ListMonitors()
    {
        var monitors = EnumerateMonitors();
        var result = new List<object>();
        for (var i = 0; i < monitors.Count; i++)
        {
            result.Add(new
            {
                device_idx = 0,
                output_idx = i,
                name = $"Monitor {i + 1}",
                resolution = new[] { monitors[i].Width, monitors[i].Height },
                refresh_rate = 60,
            });
        }
        return DriverResult.Ok(result);
    }

    private DriverResult StartStream(Dictionary<string, object?> parameters)
    {
        var streamId = GetString(parameters, "stream_id", "");
        if (string.IsNullOrEmpty(streamId))
        {
            return DriverResult.Error("stream_id is required");
        }

        lock (_lock)
        {
            if (_streams.ContainsKey(streamId))
            {
                return DriverResult.Error($"stream {streamId} already exists");
            }
        }

        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _streams[streamId] = cts;
        }

        var targetFps = GetInt(parameters, "target_fps", 30);
        var fmt = GetString(parameters, "format", "jpeg");
        var quality = GetInt(parameters, "quality", 80);
        var scale = GetDouble(parameters, "scale", 1.0);
        var monitor = GetInt(parameters, "monitor", 0);

        _ = Task.Run(async () =>
        {
            var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(targetFps, 1));
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var tick = DateTime.UtcNow;
                    using var bitmap = CaptureMonitor(monitor);
                    if (bitmap is not null)
                    {
                        byte[] data;
                        if (fmt.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            data = EncodeJpeg(bitmap, quality);
                        }
                        else
                        {
                            using var ms = new MemoryStream();
                            bitmap.Save(ms, ImageFormat.Png);
                            data = ms.ToArray();
                        }

                        FrameCaptured?.Invoke(this, (
                            streamId,
                            DriverResult.Ok(
                                new { format = fmt, width = bitmap.Width, height = bitmap.Height },
                                data,
                                $"image/{fmt.ToLowerInvariant()}")));
                    }

                    var elapsed = DateTime.UtcNow - tick;
                    var delay = interval - elapsed;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (Exception ex)
            {
                FrameCaptured?.Invoke(this, (streamId, DriverResult.Error(ex.Message)));
            }
        }, cts.Token);

        return DriverResult.Ok(new { stream_id = streamId, target_fps = targetFps });
    }

    private DriverResult StopStream(Dictionary<string, object?> parameters)
    {
        var streamId = GetString(parameters, "stream_id", "");
        if (string.IsNullOrEmpty(streamId))
        {
            return DriverResult.Error("stream_id is required");
        }

        CancellationTokenSource? cts = null;
        lock (_lock)
        {
            if (_streams.TryGetValue(streamId, out cts))
            {
                _streams.Remove(streamId);
            }
        }
        cts?.Cancel();
        cts?.Dispose();

        return DriverResult.Ok(new { stream_id = streamId, message = "stream stopped" });
    }

    public void Stop()
    {
        List<CancellationTokenSource> sources;
        lock (_lock)
        {
            sources = _streams.Values.ToList();
            _streams.Clear();
        }
        foreach (var cts in sources)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1)
        {
            Param = { [0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100)) }
        };
        using var ms = new MemoryStream();
        bitmap.Save(ms, codec, encoderParams);
        return ms.ToArray();
    }

    // ===== 显示器枚举 =====

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static List<Rectangle> EnumerateMonitors()
    {
        var monitors = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr data) =>
        {
            monitors.Add(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            var w = GetSystemMetrics(0);
            var h = GetSystemMetrics(1);
            monitors.Add(new Rectangle(0, 0, w, h));
        }
        return monitors;
    }

    private static Rectangle? GetMonitorBounds(int index)
    {
        var monitors = EnumerateMonitors();
        if (index >= 0 && index < monitors.Count)
        {
            return monitors[index];
        }
        return null;
    }

    private static string GetString(Dictionary<string, object?> dict, string key, string defaultValue)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            return value.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static int GetInt(Dictionary<string, object?> dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            return int.TryParse(value.ToString(), out var result) ? result : defaultValue;
        }
        return defaultValue;
    }

    private static double GetDouble(Dictionary<string, object?> dict, string key, double defaultValue)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            return double.TryParse(value.ToString(), out var result) ? result : defaultValue;
        }
        return defaultValue;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
