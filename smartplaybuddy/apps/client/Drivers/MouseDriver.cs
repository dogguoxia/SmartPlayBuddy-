using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SmartPlayBuddy.Client.Drivers;

public class MouseDriver : IDriver
{
    public string Name => "mouse";
    public string Description => "Mouse driver (SendInput)";

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int WHEEL_DELTA = 120;

    public async Task<DriverResult> OperateAsync(Dictionary<string, object?> parameters)
    {
        var op = GetString(parameters, "operate", "").ToLowerInvariant();

        switch (op)
        {
            case "move":
            {
                var (dx, dy) = ToPixels(parameters);
                SendMouse(new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE });
                break;
            }
            case "move_to":
            {
                var (x, y) = ToPixels(parameters);
                var w = GetSystemMetrics(0);
                var h = GetSystemMetrics(1);
                var absX = (int)(x * 65535.0 / Math.Max(w - 1, 1));
                var absY = (int)(y * 65535.0 / Math.Max(h - 1, 1));
                SendMouse(new MOUSEINPUT { dx = absX, dy = absY, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE });
                break;
            }
            case "tap":
            {
                var (down, up) = ButtonFlags(GetString(parameters, "button", "left"));
                SendMouse(new MOUSEINPUT { dwFlags = down });
                SendMouse(new MOUSEINPUT { dwFlags = up });
                break;
            }
            case "press":
            {
                var (down, _) = ButtonFlags(GetString(parameters, "button", "left"));
                SendMouse(new MOUSEINPUT { dwFlags = down });
                break;
            }
            case "release":
            {
                var (_, up) = ButtonFlags(GetString(parameters, "button", "left"));
                SendMouse(new MOUSEINPUT { dwFlags = up });
                break;
            }
            case "scroll":
            {
                var (_, dy) = ToPixels(parameters);
                SendMouse(new MOUSEINPUT { mouseData = (uint)(dy * WHEEL_DELTA), dwFlags = MOUSEEVENTF_WHEEL });
                break;
            }
            default:
                return DriverResult.Error($"unknown operate: {op}");
        }

        await Task.CompletedTask;
        return DriverResult.Ok();
    }

    private static (uint down, uint up) ButtonFlags(string button)
    {
        return button.ToLowerInvariant() switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };
    }

    private static (int x, int y) ToPixels(Dictionary<string, object?> parameters)
    {
        var w = GetSystemMetrics(0);
        var h = GetSystemMetrics(1);
        var x = (int)(GetDouble(parameters, "x", 0) * w);
        var y = (int)(GetDouble(parameters, "y", 0) * h);
        return (x, y);
    }

    private static void SendMouse(MOUSEINPUT mi)
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = mi }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static string GetString(Dictionary<string, object?> dict, string key, string defaultValue)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            return value.ToString() ?? defaultValue;
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
}
