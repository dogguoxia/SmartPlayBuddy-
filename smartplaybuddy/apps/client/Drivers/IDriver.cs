using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartPlayBuddy.Client.Drivers;

public class DriverResult
{
    public string Status { get; set; } = "ok";
    public object? Result { get; set; }
    public byte[]? Data { get; set; }
    public string? Mime { get; set; }
    public string? Message { get; set; }

    public static DriverResult Ok(object? result = null, byte[]? data = null, string? mime = null)
        => new() { Status = "ok", Result = result, Data = data, Mime = mime };

    public static DriverResult Error(string message)
        => new() { Status = "error", Message = message };
}

public interface IDriver
{
    string Name { get; }
    string Description { get; }

    Task<DriverResult> OperateAsync(Dictionary<string, object?> parameters);

    void Start() { }
    void Stop() { }
}
