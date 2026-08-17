using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SmartPlayBuddy.Client.Drivers;

public class KeyboardDriver : IDriver
{
    public string Name => "keyboard";
    public string Description => "Keyboard driver (SendInput)";

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 0x41, ["b"] = 0x42, ["c"] = 0x43, ["d"] = 0x44, ["e"] = 0x45,
        ["f"] = 0x46, ["g"] = 0x47, ["h"] = 0x48, ["i"] = 0x49, ["j"] = 0x4A,
        ["k"] = 0x4B, ["l"] = 0x4C, ["m"] = 0x4D, ["n"] = 0x4E, ["o"] = 0x4F,
        ["p"] = 0x50, ["q"] = 0x51, ["r"] = 0x52, ["s"] = 0x53, ["t"] = 0x54,
        ["u"] = 0x55, ["v"] = 0x56, ["w"] = 0x57, ["x"] = 0x58, ["y"] = 0x59,
        ["z"] = 0x5A,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73, ["f5"] = 0x74,
        ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77, ["f9"] = 0x78, ["f10"] = 0x79,
        ["f11"] = 0x7A, ["f12"] = 0x7B, ["f13"] = 0x7C, ["f14"] = 0x7D, ["f15"] = 0x7E,
        ["f16"] = 0x7F, ["f17"] = 0x80, ["f18"] = 0x81, ["f19"] = 0x82, ["f20"] = 0x83,
        ["f21"] = 0x84, ["f22"] = 0x85, ["f23"] = 0x86, ["f24"] = 0x87,
        ["ctrl"] = 0x11, ["control"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10,
        ["win"] = 0x5B, ["lwin"] = 0x5B, ["rwin"] = 0x5C,
        ["tab"] = 0x09, ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B,
        ["escape"] = 0x1B, ["space"] = 0x20, ["spacebar"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D,
        ["ins"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21,
        ["pagedown"] = 0x22, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25,
        ["right"] = 0x27, ["capslock"] = 0x14, ["caps"] = 0x14,
        ["printscreen"] = 0x2C, ["printscr"] = 0x2C, ["scrolllock"] = 0x91,
        ["numlock"] = 0x90, ["pause"] = 0x13, ["break"] = 0x13,
        ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD,
        ["\\"] = 0xDC, [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE,
        ["/"] = 0xBF,
    };

    public async Task<DriverResult> OperateAsync(Dictionary<string, object?> parameters)
    {
        var op = GetString(parameters, "operate", "tap").ToLowerInvariant();

        switch (op)
        {
            case "tap":
            {
                var key = GetString(parameters, "key", "");
                Tap(key);
                break;
            }
            case "combo":
            {
                var keys = GetStringList(parameters, "keys");
                Hotkey(keys);
                break;
            }
            case "press":
            {
                var key = GetString(parameters, "key", "");
                KeyEvent(key, isDown: true);
                break;
            }
            case "release":
            {
                var key = GetString(parameters, "key", "");
                KeyEvent(key, isDown: false);
                break;
            }
            default:
                return DriverResult.Error($"unknown operate: {op}");
        }

        await Task.CompletedTask;
        return DriverResult.Ok();
    }

    private void Tap(string key)
    {
        KeyEvent(key, isDown: true);
        KeyEvent(key, isDown: false);
    }

    private void Hotkey(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            KeyEvent(key, isDown: true);
        }
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            KeyEvent(keys[i], isDown: false);
        }
    }

    private void KeyEvent(string key, bool isDown)
    {
        if (!VkMap.TryGetValue(key, out var vk))
        {
            if (key.Length == 1)
            {
                vk = (ushort)char.ToUpperInvariant(key[0]);
            }
            else
            {
                return;
            }
        }

        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = isDown ? 0 : KEYEVENTF_KEYUP,
                    }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static string GetString(Dictionary<string, object?> dict, string key, string defaultValue)
    {
        if (dict.TryGetValue(key, out var value) && value is not null)
        {
            return value.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static List<string> GetStringList(Dictionary<string, object?> dict, string key)
    {
        var result = new List<string>();
        if (dict.TryGetValue(key, out var value) && value is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    result.Add(item.GetString() ?? "");
                }
            }
        }
        return result;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public MOUSEINPUT mi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}
