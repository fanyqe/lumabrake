using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Monitors;

namespace LumaBrake.Rendering;

public interface IOverlayRenderer : IDisposable
{
    void SyncMonitors(IReadOnlyList<MonitorDescriptor> monitors);

    void Show();

    void SetEffect(string monitorStableId, FinalEffectState effect);

    void HideAll();
}
