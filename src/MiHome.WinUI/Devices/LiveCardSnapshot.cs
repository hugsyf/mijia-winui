using MiHome.WinUI.Core;
namespace MiHome.WinUI.Devices;

internal sealed class LiveCardSnapshot(DeviceCardState? initial)
{
    public DeviceCardState? Value { get; private set; } = initial;
    public event Action? Changed;
    public void Set(DeviceCardState? value)
    {
        if (ReferenceEquals(Value,value)) return;
        Value=value; Changed?.Invoke();
    }
}
