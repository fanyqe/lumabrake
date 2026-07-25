using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public interface IOverlayRenderer : IDisposable
{
    void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors);

    void Show();

    void SetOpacity(string monitorStableId, double opacity);

    void HideAll();
}
