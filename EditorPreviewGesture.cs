namespace StructureHandler;

// A drag only belongs to the preview if the same button was pressed inside
// it. A field, panel, or the previous document may have received the press.
internal sealed class EditorPreviewGesture
{
    private bool left, middle;
    internal bool Owns(int button) => button == 0 ? left : button == 2 && middle;
    internal void Set(int button, bool inside)
    {
        if (button == 0) left = inside;
        if (button == 2) middle = inside;
    }
    internal void Reset() => left = middle = false;
}
