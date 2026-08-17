using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartPlayBuddy.Client.Drivers;

public class DriverRegistry
{
    private readonly Dictionary<string, IDriver> _drivers = new();

    public DriverRegistry()
    {
        Register(new KeyboardDriver());
        Register(new MouseDriver());
        Register(new ScreenDriver());
    }

    public void Register(IDriver driver)
    {
        _drivers[driver.Name] = driver;
    }

    public bool Contains(string name) => _drivers.ContainsKey(name);

    public IReadOnlyCollection<string> Names => _drivers.Keys;

    public ScreenDriver? GetScreenDriver()
    {
        return _drivers.TryGetValue("screen", out var driver) ? driver as ScreenDriver : null;
    }

    public async Task<DriverResult> ExecuteAsync(string name, Dictionary<string, object?> parameters)
    {
        if (!_drivers.TryGetValue(name, out var driver))
        {
            return DriverResult.Error($"unknown driver: {name}");
        }

        try
        {
            return await driver.OperateAsync(parameters);
        }
        catch (Exception ex)
        {
            return DriverResult.Error(ex.Message);
        }
    }

    public void StopAll()
    {
        foreach (var driver in _drivers.Values)
        {
            driver.Stop();
        }
    }
}
