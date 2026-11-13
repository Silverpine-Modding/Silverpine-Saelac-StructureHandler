#nullable enable
namespace StructureHandler;

internal sealed class EditorCoordinateDraft
{
    private string? observed;
    internal string Text = "";
    internal void Observe(string current)
    {
        if (observed == current) return;
        observed = current;
        Text = current;
    }
    internal void Reset() { observed = null; }
}
