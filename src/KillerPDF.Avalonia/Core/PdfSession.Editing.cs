using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;

namespace KillerPDF.Avalonia.Core;

public sealed partial class PdfSession
{
    public const int UndoLimit = 50;
    private readonly LinkedList<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Rotate(IReadOnlyCollection<int> pages, int degreesClockwise)
    {
        int turns = ((degreesClockwise % 360) + 360) % 360 / 90;
        if (turns == 0 || pages.Count == 0) return;
        Apply(editor =>
        {
            foreach (int page in pages.Distinct())
                for (int turn = 0; turn < turns; turn++) editor.RotateClockwise(page);
        });
    }

    public void Delete(IReadOnlyCollection<int> pages)
    {
        int[] removed = [.. pages.Distinct().OrderByDescending(index => index)];
        if (removed.Length == 0) return;
        if (removed.Length >= PageCount)
            throw new InvalidOperationException("A document must keep at least one page.");
        Apply(editor => { foreach (int page in removed) editor.RemovePage(page); });
    }

    public void Move(int from, int to)
    {
        if (from == to) return;
        Apply(editor => editor.MovePage(from, to));
    }

    public void Merge(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        List<PdfDocument> sources = [.. paths.Select(path =>
            PdfDocument.OpenWithCompatibilityRecovery(File.ReadAllBytes(path)))];
        Apply(editor => { foreach (PdfDocument source in sources) editor.AddImportedDocument(source); });
    }

    public void Undo()
    {
        if (_undo.Last is not { } last) return;
        _undo.RemoveLast();
        _redo.Push(_bytes);
        Load(last.Value);
        Raise();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        PushUndo(_bytes);
        Load(_redo.Pop());
        Raise();
    }

    private void Apply(Action<PdfIncrementalPageEditor> edit)
    {
        byte[] next;
        lock (Document)
        {
            var editor = new PdfIncrementalPageEditor(Document);
            edit(editor);
            next = editor.Build();
        }
        byte[] previous = _bytes;
        Load(next);
        PushUndo(previous);
        _redo.Clear();
        Raise();
    }

    private void PushUndo(byte[] bytes)
    {
        _undo.AddLast(bytes);
        if (_undo.Count > UndoLimit) _undo.RemoveFirst();
    }
}
