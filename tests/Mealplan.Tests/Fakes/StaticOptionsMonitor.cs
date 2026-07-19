using Microsoft.Extensions.Options;

namespace Mealplan.Tests.Fakes;

/// <summary>Returns one options instance whatever name is asked for.</summary>
public class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
