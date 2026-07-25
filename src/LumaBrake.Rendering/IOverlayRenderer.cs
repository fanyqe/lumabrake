namespace LumaBrake.Rendering;

public interface IOverlayRenderer : IDisposable
{
    void Show();

    void SetOpacity(double opacity);

    void HideOverlay();
}
