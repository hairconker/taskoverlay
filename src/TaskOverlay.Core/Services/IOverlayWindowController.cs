namespace TaskOverlay.Core.Services;

public interface IOverlayWindowController
{
    bool IsTopmost { get; }
    bool IsEditMode { get; }
    void SetTopmost(bool enabled);
    void SetClickThrough(bool enabled);
    void SetOpacity(double opacity);
    void ShowOverlay();
    void HideOverlay();
    void ToggleEditMode();
}
