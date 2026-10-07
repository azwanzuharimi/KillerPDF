# KillerPDF macOS Prototype Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Avalonia app for macOS arm64 that views PDFs and does rotate, delete, reorder, merge, save with KillerPdf.Engine, packaged as `.app` and `.dmg`, plus evidence numbers for discussion #320.

**Architecture:** One Avalonia project `src/KillerPDF.Avalonia` with a UI-free `Core/` folder (`PdfSession`, `PageRasterizer`, `MacFontResolver`, `BatchRender`) and an Avalonia UI on top. One xUnit project `src/KillerPDF.Avalonia.Tests` tests `Core/`. The engine is a `ProjectReference`. Only `Services/PdfFontStyle.cs` is linked from the WPF app.

**Tech Stack:** .NET SDK 10.0.400 (`~/.dotnet/dotnet`), Avalonia 12.1.3, xUnit 2.9.3, KillerPdf.Engine 1.9.0.

**Spec:** `src/KillerPDF.Avalonia/DESIGN.md`

## Global Constraints

- Change no file outside `src/KillerPDF.Avalonia/`, `src/KillerPDF.Avalonia.Tests/` and `.github/workflows/mac-prototype.yml`. Do not edit `KillerPDF.sln`, `KillerPDF.csproj`, or any engine file.
- Do not change any `<Version>`. The engine build fails if engine and app versions differ.
- Link only `Services/PdfFontStyle.cs`. Never link `PdfEngineIntegration.cs` (uses `System.Drawing`).
- `Info.plist` PDF document type uses `LSHandlerRank` = `Alternate`. Never `Owner` or `Default`.
- Use `export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1` before every `dotnet` command.
- Code comments: avoid them. If one is necessary, one line only, ASD-STE100 style.
- Commit messages: ASD-STE100 (simple words, active voice, imperative, max 20 words per sentence). End each commit message with a blank line and `Claude-Session: https://claude.ai/code/session_01EtnXq1iCVU5GTMUrrc6AqM`.
- When unsure of an Avalonia 12 API, look it up with Context7 (`mcp__claude_ai_Context7__query-docs`, library `/avaloniaui/avalonia-docs`) before writing code. Do not guess.

## Review Focus

1. A PDF that the engine cannot open (damaged, encrypted): the app shows a message and stays open; `PdfSession.Open` throws, it does not return a half-built session. Test in Task 1.
2. A page with `/Rotate 90` or `270`: the rendered bitmap is landscape and not stretched. Test in Task 4.
3. Save to a folder that does not exist or is read only: the in-memory document and `IsDirty` stay unchanged. Test in Task 1.
4. Delete of every page: rejected, document unchanged. Test in Task 2.
5. Fast scrolling or zooming in a 500-page file: old render jobs are cancelled; memory stays bounded because only pages near the viewport keep bitmaps. Manual check in Task 5 with the measurement recorded in Task 9.

---

### Task 1: Projects, PdfSession open and save

**Files:**
- Create: `src/KillerPDF.Avalonia/KillerPDF.Avalonia.csproj`
- Create: `src/KillerPDF.Avalonia/Core/PdfSession.cs`
- Create: `src/KillerPDF.Avalonia.Tests/KillerPDF.Avalonia.Tests.csproj`
- Create: `src/KillerPDF.Avalonia.Tests/TestPdf.cs`
- Create: `src/KillerPDF.Avalonia.Tests/PdfSessionTests.cs`

**Interfaces:**
- Produces: `KillerPDF.Avalonia.Core.PdfSession` with `static PdfSession Open(string path)`, `string FilePath`, `PdfDocument Document`, `IReadOnlyList<PdfPageInformation> Pages`, `int PageCount`, `bool IsDirty`, `int Version`, `event EventHandler? Changed`, `void Save()`, `void SaveAs(string path)`.
- Produces: test helper `TestPdf.Create(string path, params double[] pageWidths)` writes a PDF whose page i has width `pageWidths[i]` and height 500. Tests identify page order by width.

- [ ] **Step 1: Create the app project**

`src/KillerPDF.Avalonia/KillerPDF.Avalonia.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AssemblyName>KillerPDF</AssemblyName>
    <RootNamespace>KillerPDF.Avalonia</RootNamespace>
    <ApplicationManifest />
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.3" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../engine/KillerPdf.Engine/KillerPdf.Engine.csproj" />
    <Compile Include="../../Services/PdfFontStyle.cs" Link="Linked/PdfFontStyle.cs" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="KillerPDF.Avalonia.Tests" />
  </ItemGroup>
</Project>
```

Until Task 5 adds `App`, add a temporary `src/KillerPDF.Avalonia/Program.cs`:

```csharp
namespace KillerPDF.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => 0;
}
```

- [ ] **Step 2: Create the test project and fixture helper**

`src/KillerPDF.Avalonia.Tests/KillerPDF.Avalonia.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../KillerPDF.Avalonia/KillerPDF.Avalonia.csproj" />
    <ProjectReference Include="../../engine/KillerPdf.Engine/KillerPdf.Engine.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`src/KillerPDF.Avalonia.Tests/TestPdf.cs`:

```csharp
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;

namespace KillerPDF.Avalonia.Tests;

internal static class TestPdf
{
    internal static string Create(string path, params double[] pageWidths)
    {
        var builder = new PdfDocumentBuilder();
        foreach (double width in pageWidths) builder.AddPage(width, 500, ReadOnlyMemory<byte>.Empty);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    internal static double[] Widths(string path) =>
        [.. PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(path))).Select(p => p.Width)];

    internal static int[] Rotations(string path) =>
        [.. PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(path))).Select(p => p.Rotation)];

    internal static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kp-mac-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

If `PdfDocumentBuilder` has no parameterless constructor or `AddPage` rejects empty content, read `engine/KillerPdf.Engine/Authoring/PdfDocumentBuilder.cs` and engine tests that call `AddPage`, and copy their pattern.

- [ ] **Step 3: Write failing tests**

`src/KillerPDF.Avalonia.Tests/PdfSessionTests.cs`:

```csharp
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PdfSessionTests
{
    private readonly string _dir = TestPdf.TempDir();

    [Fact]
    public void OpenReadsPagesAndIsClean()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100, 110, 120);
        PdfSession session = PdfSession.Open(file);
        Assert.Equal(3, session.PageCount);
        Assert.Equal([100d, 110d, 120d], session.Pages.Select(p => p.Width));
        Assert.False(session.IsDirty);
        Assert.Equal(Path.GetFullPath(file), session.FilePath);
    }

    [Fact]
    public void OpenOfGarbageThrows()
    {
        string file = Path.Combine(_dir, "bad.pdf");
        File.WriteAllText(file, "this is not a pdf");
        Assert.ThrowsAny<Exception>(() => PdfSession.Open(file));
    }

    [Fact]
    public void SaveAsWritesFileAndClearsDirty()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100, 110);
        PdfSession session = PdfSession.Open(file);
        string target = Path.Combine(_dir, "out.pdf");
        session.SaveAs(target);
        Assert.Equal([100d, 110d], TestPdf.Widths(target));
        Assert.Equal(target, session.FilePath);
        Assert.False(session.IsDirty);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveAsToMissingFolderThrowsAndKeepsState()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100);
        PdfSession session = PdfSession.Open(file);
        Assert.ThrowsAny<IOException>(() => session.SaveAs(Path.Combine(_dir, "missing", "x.pdf")));
        Assert.Equal(Path.GetFullPath(file), session.FilePath);
    }
}
```

- [ ] **Step 4: Run tests, expect build failure (PdfSession missing)**

Run: `cd ~/projects/KillerPDF && dotnet test src/KillerPDF.Avalonia.Tests`
Expected: compile error `The type or namespace name 'PdfSession' could not be found`.

- [ ] **Step 5: Implement PdfSession (open and save part)**

`src/KillerPDF.Avalonia/Core/PdfSession.cs`:

```csharp
using KillerPdf.Engine.Documents;

namespace KillerPDF.Avalonia.Core;

public sealed partial class PdfSession
{
    private byte[] _bytes = [];
    private byte[] _savedBytes = [];

    private PdfSession(string path, byte[] bytes)
    {
        FilePath = Path.GetFullPath(path);
        Load(bytes);
        _savedBytes = bytes;
    }

    public string FilePath { get; private set; }
    public PdfDocument Document { get; private set; } = null!;
    public IReadOnlyList<PdfPageInformation> Pages { get; private set; } = [];
    public int PageCount => Pages.Count;
    public bool IsDirty => !ReferenceEquals(_bytes, _savedBytes);
    public int Version { get; private set; }
    public event EventHandler? Changed;

    public static PdfSession Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PdfSession(path, File.ReadAllBytes(path));
    }

    public void Save() => SaveAs(FilePath);

    public void SaveAs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        string temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, _bytes);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        FilePath = full;
        _savedBytes = _bytes;
        Raise();
    }

    private void Load(byte[] bytes)
    {
        PdfDocument document = PdfDocument.OpenWithCompatibilityRecovery(bytes);
        IReadOnlyList<PdfPageInformation> pages = PdfPageInformation.Read(document);
        if (pages.Count == 0) throw new InvalidDataException("The PDF contains no pages.");
        Document = document;
        Pages = pages;
        _bytes = bytes;
        Version++;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 6: Run tests, expect pass**

Run: `dotnet test src/KillerPDF.Avalonia.Tests`
Expected: 4 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/KillerPDF.Avalonia src/KillerPDF.Avalonia.Tests
git commit -m "Add macOS prototype projects and PdfSession open and save"
```

---

### Task 2: PdfSession edits, undo and redo

**Files:**
- Create: `src/KillerPDF.Avalonia/Core/PdfSession.Editing.cs`
- Create: `src/KillerPDF.Avalonia.Tests/PdfSessionEditingTests.cs`

**Interfaces:**
- Consumes: `PdfSession` from Task 1 (`Load`, `_bytes`, `Raise` are private members of the same partial class).
- Produces: `void Rotate(IReadOnlyCollection<int> pages, int degreesClockwise)`, `void Delete(IReadOnlyCollection<int> pages)`, `void Move(int from, int to)` (`to` = final zero-based index), `void Merge(IReadOnlyList<string> paths)`, `void Undo()`, `void Redo()`, `bool CanUndo`, `bool CanRedo`, `const int UndoLimit = 50`.

- [ ] **Step 1: Write failing tests**

`src/KillerPDF.Avalonia.Tests/PdfSessionEditingTests.cs`:

```csharp
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PdfSessionEditingTests
{
    private readonly string _dir = TestPdf.TempDir();

    private PdfSession Open(params double[] widths) =>
        PdfSession.Open(TestPdf.Create(Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf"), widths));

    [Fact]
    public void RotateChangesOnlySelectedPages()
    {
        PdfSession s = Open(100, 110, 120);
        s.Rotate([1], 90);
        Assert.Equal([0, 90, 0], s.Pages.Select(p => p.Rotation));
        Assert.True(s.IsDirty);
        s.Rotate([1], -90);
        Assert.Equal(0, s.Pages[1].Rotation);
    }

    [Fact]
    public void DeleteRemovesPagesAndKeepsOrder()
    {
        PdfSession s = Open(100, 110, 120, 130);
        s.Delete([0, 2]);
        Assert.Equal([110d, 130d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void DeleteOfEveryPageIsRejected()
    {
        PdfSession s = Open(100, 110);
        Assert.Throws<InvalidOperationException>(() => s.Delete([0, 1]));
        Assert.Equal(2, s.PageCount);
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void MovePutsPageAtFinalIndex()
    {
        PdfSession s = Open(100, 110, 120);
        s.Move(0, 2);
        Assert.Equal([110d, 120d, 100d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void MergeAppendsPagesInOrder()
    {
        PdfSession s = Open(100);
        string b = TestPdf.Create(Path.Combine(_dir, "b.pdf"), 200, 210);
        string c = TestPdf.Create(Path.Combine(_dir, "c.pdf"), 300);
        s.Merge([b, c]);
        Assert.Equal([100d, 200d, 210d, 300d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void UndoAndRedoRestoreStateAndDirtyFlag()
    {
        PdfSession s = Open(100, 110, 120);
        s.Delete([0]);
        s.Move(0, 1);
        s.Undo();
        Assert.Equal([110d, 120d], s.Pages.Select(p => p.Width));
        s.Undo();
        Assert.Equal([100d, 110d, 120d], s.Pages.Select(p => p.Width));
        Assert.False(s.IsDirty);
        Assert.False(s.CanUndo);
        s.Redo();
        Assert.Equal([110d, 120d], s.Pages.Select(p => p.Width));
        Assert.True(s.IsDirty);
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        PdfSession s = Open(100, 110);
        s.Rotate([0], 90);
        s.Undo();
        s.Rotate([1], 90);
        Assert.False(s.CanRedo);
    }

    [Fact]
    public void UndoHistoryIsCapped()
    {
        PdfSession s = Open(100, 110);
        for (int i = 0; i < PdfSession.UndoLimit + 5; i++) s.Rotate([0], 90);
        int undone = 0;
        while (s.CanUndo) { s.Undo(); undone++; }
        Assert.Equal(PdfSession.UndoLimit, undone);
    }

    [Fact]
    public void OriginalFileIsUnchangedUntilSave()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "orig.pdf"), 100, 110, 120);
        byte[] before = File.ReadAllBytes(file);
        PdfSession s = PdfSession.Open(file);
        s.Delete([1]);
        s.Rotate([0], 90);
        Assert.Equal(before, File.ReadAllBytes(file));
        s.Save();
        Assert.Equal([100d, 120d], TestPdf.Widths(file));
        Assert.Equal([90, 0], TestPdf.Rotations(file));
    }
}
```

- [ ] **Step 2: Run tests, expect compile failure**

Run: `dotnet test src/KillerPDF.Avalonia.Tests`
Expected: compile errors for `Rotate`, `Delete`, `Move`, `Merge`, `Undo`, `Redo`.

- [ ] **Step 3: Implement editing**

`src/KillerPDF.Avalonia/Core/PdfSession.Editing.cs`:

```csharp
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
        var editor = new PdfIncrementalPageEditor(Document);
        edit(editor);
        byte[] next = editor.Build();
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
```

- [ ] **Step 4: Run tests, expect pass**

Run: `dotnet test src/KillerPDF.Avalonia.Tests`
Expected: 13 passed. If `RotateClockwise` on a page that already has rotation from an earlier incremental update gives a wrong value, read `PdfIncrementalPageEditor.Rotate` (line ~4464) and fix the session, not the engine.

- [ ] **Step 5: Commit**

```bash
git add src/KillerPDF.Avalonia/Core/PdfSession.Editing.cs src/KillerPDF.Avalonia.Tests/PdfSessionEditingTests.cs
git commit -m "Add rotate, delete, move, merge, undo and redo to PdfSession"
```

---

### Task 3: MacFontResolver

**Files:**
- Create: `src/KillerPDF.Avalonia/Core/MacFontResolver.cs`
- Create: `src/KillerPDF.Avalonia.Tests/MacFontResolverTests.cs`

**Interfaces:**
- Consumes: linked `KillerPDF.Services.PdfFontStyle.FromPdfName(string)` → `DetectedPdfFontStyle(string Family, bool Bold, bool Italic)`. It maps `Helvetica`→`Arial`, `Times`→`Times New Roman`, `Courier`→`Courier New`.
- Produces: `MacFontResolver : IPdfFontResolver`, `static MacFontResolver Instance`, constructor `MacFontResolver(IReadOnlyList<string> fontDirectories)`.

- [ ] **Step 1: Write failing tests**

```csharp
using KillerPdf.Engine.Fonts;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class MacFontResolverTests
{
    private static PdfFontRequest Req(string name) => new(name, "", "", false);

    [Fact]
    public void ResolvesStandardAliasesToFileBytes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        byte[]? regular = MacFontResolver.Instance.Resolve(Req("Helvetica"));
        byte[]? bold = MacFontResolver.Instance.Resolve(Req("ArialMT,Bold"));
        Assert.NotNull(regular);
        Assert.NotNull(bold);
        Assert.NotEqual(regular!.Length, bold!.Length);
    }

    [Fact]
    public void FindsFaceInGivenFolderBySubsetName()
    {
        string dir = TestPdf.TempDir();
        File.WriteAllBytes(Path.Combine(dir, "Courier New Bold Italic.ttf"), [1, 2, 3]);
        var resolver = new MacFontResolver([dir]);
        Assert.Equal([1, 2, 3], resolver.Resolve(Req("ABCDEF+CourierNewPS-BoldItalicMT")));
    }

    [Fact]
    public void UnknownFontReturnsNull()
    {
        var resolver = new MacFontResolver([TestPdf.TempDir()]);
        Assert.Null(resolver.Resolve(Req("NoSuchFont-Regular")));
    }
}
```

- [ ] **Step 2: Run, expect compile failure**

Run: `dotnet test src/KillerPDF.Avalonia.Tests --filter MacFontResolverTests`

- [ ] **Step 3: Implement**

```csharp
using KillerPdf.Engine.Fonts;
using KillerPDF.Services;

namespace KillerPDF.Avalonia.Core;

public sealed class MacFontResolver(IReadOnlyList<string> fontDirectories) : IPdfFontResolver
{
    public static MacFontResolver Instance { get; } = new([
        "/System/Library/Fonts/Supplemental",
        "/System/Library/Fonts",
        "/Library/Fonts",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"),
    ]);

    private readonly Dictionary<PdfFontRequest, byte[]?> _cache = [];
    private readonly Lock _gate = new();

    public byte[]? Resolve(PdfFontRequest request)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(request, out byte[]? cached)) return cached;
            DetectedPdfFontStyle style = PdfFontStyle.FromPdfName(request.PostScriptName);
            string face = (style.Bold, style.Italic) switch
            {
                (true, true) => " Bold Italic",
                (true, false) => " Bold",
                (false, true) => " Italic",
                _ => "",
            };
            byte[]? bytes = null;
            foreach (string directory in fontDirectories)
            {
                foreach (string extension in new[] { ".ttf", ".otf" })
                {
                    string path = Path.Combine(directory, style.Family + face + extension);
                    if (!File.Exists(path)) continue;
                    try { bytes = File.ReadAllBytes(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    if (bytes is not null) break;
                }
                if (bytes is not null) break;
            }
            _cache[request] = bytes;
            return bytes;
        }
    }
}
```

If `FromPdfName("ABCDEF+CourierNewPS-BoldItalicMT").Family` is not `"Courier New"`, print it in the test, then adjust only the resolver (for example also try `PdfFontStyle.ResolveInstalledFamily`). Do not edit `PdfFontStyle.cs`.

- [ ] **Step 4: Run, expect pass**

Run: `dotnet test src/KillerPDF.Avalonia.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/KillerPDF.Avalonia/Core/MacFontResolver.cs src/KillerPDF.Avalonia.Tests/MacFontResolverTests.cs
git commit -m "Add macOS font resolver for engine rendering"
```

---

### Task 4: PageRasterizer and headless batch render

**Files:**
- Create: `src/KillerPDF.Avalonia/Core/PageRasterizer.cs`
- Create: `src/KillerPDF.Avalonia/Core/BatchRender.cs`
- Modify: `src/KillerPDF.Avalonia/Program.cs`
- Create: `src/KillerPDF.Avalonia.Tests/PageRasterizerTests.cs`
- Create: `src/KillerPDF.Avalonia.Tests/BatchRenderTests.cs`

**Interfaces:**
- Consumes: `MacFontResolver.Instance`, `PdfSession.Document`, `PdfSession.Pages`.
- Produces: `PageRasterizer(PdfDocument document)`; `static (int Width, int Height) PixelSize(PdfPageInformation page, double scale)` (swaps width and height when `Rotation` is 90 or 270, minimum 1 pixel); `RasterPage Render(int pageIndex, double scale, CancellationToken token)`; `record RasterPage(int Width, int Height, byte[] Bgra, IReadOnlyList<string> Diagnostics)`.
- Produces: `BatchRender.Run(string folder, string csvPath, double scale, TextWriter log)` returns exit code 0 when every file rendered, 1 when any file failed. CSV header `file,page,width,height,ms,error`.
- Produces: `Program.Main` runs `--render-folder <dir> --out <csv> [--scale 1.5]` without UI.

- [ ] **Step 1: Write failing tests**

```csharp
using KillerPdf.Engine.Documents;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PageRasterizerTests
{
    [Fact]
    public void PixelSizeSwapsForQuarterTurns()
    {
        var page = new PdfPageInformation { Width = 200, Height = 100, Rotation = 90 };
        Assert.Equal((200, 400), PageRasterizer.PixelSize(page, 2));
        Assert.Equal((400, 200), PageRasterizer.PixelSize(page with { Rotation = 180 }, 2));
    }

    [Fact]
    public void RotatedPageRendersLandscapeBitmap()
    {
        string dir = TestPdf.TempDir();
        PdfSession s = PdfSession.Open(TestPdf.Create(Path.Combine(dir, "r.pdf"), 300));
        s.Rotate([0], 90);
        RasterPage page = new PageRasterizer(s.Document).Render(0, 1, CancellationToken.None);
        Assert.Equal((500, 300), (page.Width, page.Height));
        Assert.Equal(500 * 300 * 4, page.Bgra.Length);
    }

    [Fact]
    public void CancelledRenderThrows()
    {
        string dir = TestPdf.TempDir();
        PdfSession s = PdfSession.Open(TestPdf.Create(Path.Combine(dir, "c.pdf"), 300));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new PageRasterizer(s.Document).Render(0, 1, cts.Token));
    }
}

public class BatchRenderTests
{
    [Fact]
    public void WritesRowPerPageAndFailsOnBadFile()
    {
        string dir = TestPdf.TempDir();
        TestPdf.Create(Path.Combine(dir, "good.pdf"), 100, 110);
        File.WriteAllText(Path.Combine(dir, "bad.pdf"), "garbage");
        string csv = Path.Combine(dir, "out.csv");
        int code = BatchRender.Run(dir, csv, 1, TextWriter.Null);
        string[] lines = File.ReadAllLines(csv);
        Assert.Equal(1, code);
        Assert.Equal("file,page,width,height,ms,error", lines[0]);
        Assert.Equal(2, lines.Count(l => l.StartsWith("good.pdf,")));
        Assert.Contains(lines, l => l.StartsWith("bad.pdf,-1,") && l.Length > "bad.pdf,-1,0,0,0,".Length);
    }
}
```

If the renderer does not apply `/Rotate` itself (the rotated test gives a portrait bitmap or a wrong picture), stop and report it as a finding. Do not rotate pixels in the app without telling the controller.

- [ ] **Step 2: Run, expect compile failure**

- [ ] **Step 3: Implement PageRasterizer**

```csharp
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Rendering;

namespace KillerPDF.Avalonia.Core;

public sealed record RasterPage(int Width, int Height, byte[] Bgra, IReadOnlyList<string> Diagnostics);

public sealed class PageRasterizer(PdfDocument document)
{
    private readonly PdfPageRenderer _renderer = new(document, MacFontResolver.Instance);
    private readonly IReadOnlyList<PdfPageInformation> _pages = PdfPageInformation.Read(document);

    public static (int Width, int Height) PixelSize(PdfPageInformation page, double scale)
    {
        int w = Math.Max(1, (int)Math.Round(page.Width * scale));
        int h = Math.Max(1, (int)Math.Round(page.Height * scale));
        return page.Rotation is 90 or 270 ? (h, w) : (w, h);
    }

    public RasterPage Render(int pageIndex, double scale, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (int w, int h) = PixelSize(_pages[pageIndex], scale);
        var options = new PdfRenderOptions(w, h) { CacheResult = false };
        byte[] pixels = new byte[w * h * 4];
        IReadOnlyList<string> diagnostics = _renderer.RenderInto(pageIndex, options, pixels, token);
        return new RasterPage(w, h, pixels, diagnostics);
    }
}
```

- [ ] **Step 4: Implement BatchRender and Program**

```csharp
using System.Diagnostics;
using System.Globalization;

namespace KillerPDF.Avalonia.Core;

public static class BatchRender
{
    public static int Run(string folder, string csvPath, double scale, TextWriter log)
    {
        bool failed = false;
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("file,page,width,height,ms,error");
        foreach (string file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories).Order())
        {
            string name = Path.GetRelativePath(folder, file);
            try
            {
                PdfSession session = PdfSession.Open(file);
                var rasterizer = new PageRasterizer(session.Document);
                for (int i = 0; i < session.PageCount; i++)
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        RasterPage page = rasterizer.Render(i, scale, CancellationToken.None);
                        csv.WriteLine(Row(name, i, page.Width, page.Height, watch.ElapsedMilliseconds, ""));
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        csv.WriteLine(Row(name, i, 0, 0, watch.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                csv.WriteLine(Row(name, -1, 0, 0, 0, ex.GetType().Name + ": " + ex.Message));
            }
            log.WriteLine(name);
        }
        return failed ? 1 : 0;
    }

    private static string Row(string file, int page, int w, int h, long ms, string error) =>
        string.Join(',', Quote(file), page.ToString(CultureInfo.InvariantCulture), w, h, ms, Quote(error));

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
```

`src/KillerPDF.Avalonia/Program.cs`:

```csharp
using System.Globalization;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int folder = Array.IndexOf(args, "--render-folder");
        if (folder >= 0)
        {
            int outIndex = Array.IndexOf(args, "--out");
            int scaleIndex = Array.IndexOf(args, "--scale");
            if (folder + 1 >= args.Length || outIndex < 0 || outIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: KillerPDF --render-folder <dir> --out <csv> [--scale 1.5]");
                return 2;
            }
            double scale = scaleIndex >= 0 && scaleIndex + 1 < args.Length
                ? double.Parse(args[scaleIndex + 1], CultureInfo.InvariantCulture) : 1.5;
            return BatchRender.Run(args[folder + 1], args[outIndex + 1], scale, Console.Out);
        }
        return 0;
    }
}
```

- [ ] **Step 5: Run tests and a real batch**

Run: `dotnet test src/KillerPDF.Avalonia.Tests` → all pass.
Run: `mkdir -p /tmp/kp-corpus && cp KillerPDF.pdf /tmp/kp-corpus/ && dotnet run --project src/KillerPDF.Avalonia -c Release -- --render-folder /tmp/kp-corpus --out /tmp/kp-corpus/out.csv; echo exit=$?; head -5 /tmp/kp-corpus/out.csv`
Expected: `exit=0`, 50 page rows.

- [ ] **Step 6: Commit**

```bash
git add src/KillerPDF.Avalonia src/KillerPDF.Avalonia.Tests
git commit -m "Add page rasterizer and headless folder render command"
```

---

### Task 5: Avalonia shell: window, open, page view, zoom, page list

**Files:**
- Create: `src/KillerPDF.Avalonia/App.axaml`, `App.axaml.cs`
- Create: `src/KillerPDF.Avalonia/Theme/Dark.axaml`
- Create: `src/KillerPDF.Avalonia/Views/MainWindow.axaml`, `MainWindow.axaml.cs`
- Create: `src/KillerPDF.Avalonia/Views/PageView.cs`
- Create: `src/KillerPDF.Avalonia/Views/PageImageCache.cs`
- Modify: `src/KillerPDF.Avalonia/Program.cs`

**Interfaces:**
- Consumes: `PdfSession` (Tasks 1–2), `PageRasterizer`, `RasterPage` (Task 4).
- Produces: `MainWindow.OpenFile(string path)` (public, used by Task 6 and by macOS file-open events), `MainWindow.Session` (`PdfSession?`), `MainWindow.SelectedPages` (`IReadOnlyList<int>`), `MainWindow.Zoom` (double, 0.25–5.0), `MainWindow.RefreshDocument()` (rebuilds page list and view after `Session.Changed`).

Behavior to build:

1. `Program.Main`: keep the `--render-folder` branch from Task 4; otherwise `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`, with `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()` (check `WithInterFont` exists in 12.x via Context7; drop it if not).
2. `App`: `FluentTheme` with `RequestedThemeVariant="Dark"`, plus `Theme/Dark.axaml` resources copied by value from `Themes/Dark.xaml`: `BackgroundBrush #1c1c1c`, `MenuBackgroundBrush #1e1e1e`, `CardBorderBrush #2e2e2e`, `SelectionAccent #1ea54c`, `PaneBorderBrush #3f3f3f`, `InputBorderBrush #2e2e2e`. Main window title `KillerPDF`. If command line args contain a `.pdf` path, open it. Handle macOS Finder "Open With" file events: look up the Avalonia 12 API for file activation on macOS (`IActivatableLifetime` / `FileActivatedEventArgs` or equivalent) via Context7 and call `MainWindow.OpenFile`.
3. `MainWindow` layout: `DockPanel` with a toolbar (top), a status bar (bottom), a page list `ListBox` (left, width 180, `SelectionMode="Multiple,Toggle"` not needed — use `Multiple`), and a `ScrollViewer` holding a vertical `StackPanel` of `PageView` controls (center). Toolbar in this task: Open, Zoom out, zoom percent text, Zoom in, Fit width.
4. `OpenFile(path)`: `PdfSession.Open` inside try/catch; on failure show a modal message window with the file name and exception message; the app stays open. On success, subscribe `Session.Changed` → `RefreshDocument()`, and set window title to file name.
5. `PageView` (a `Control`): has `PageIndex`, a fixed size in device-independent pixels = `PageRasterizer.PixelSize(page, Zoom)`, draws a white placeholder until its bitmap arrives, then draws the `WriteableBitmap` (`PixelFormat.Bgra8888`, `AlphaFormat.Premul`) created from `RasterPage.Bgra`. On render failure draws the placeholder plus the error text.
6. Render scheduling (`PageImageCache`): after scroll, zoom or document change, compute the visible page range from the `ScrollViewer` offset and viewport, add 2 pages before and after, cancel the previous `CancellationTokenSource`, and render the missing pages on `Task.Run` with scale = `Zoom * TopLevel.RenderScaling`. Keep bitmaps only for that range; dispose others. One `PageRasterizer` per `Session.Version`.
7. Page list: one item per page with a thumbnail (scale so width is 140 px) rendered lazily the same way, and a page number label. Selecting an item scrolls the main view to that page. `SelectedPages` returns selected indices sorted.
8. Status bar: file name, `page n / N` (topmost visible page), and `•` (unsaved marker) when `Session.IsDirty`.
9. Keys: Cmd+O open, Cmd+Plus / Cmd+Minus zoom by 1.25×, Cmd+0 fit width. Use `KeyModifiers.Meta` on macOS (Avalonia maps Cmd to `Meta`; verify via Context7).

- [ ] **Step 1:** Build the files above.
- [ ] **Step 2:** Run `dotnet build src/KillerPDF.Avalonia -c Release` → 0 errors.
- [ ] **Step 3:** Run `dotnet run --project src/KillerPDF.Avalonia -c Release -- "$PWD/KillerPDF.pdf"`. Check by hand with a screenshot (`screencapture -l <windowid>` or `screencapture -x /tmp/kp.png`): first page visible and sharp, scrolling renders later pages, zoom changes size, page list shows thumbnails. Attach the screenshot path to the task report.
- [ ] **Step 4:** Open a file with garbage content; the message appears and the app keeps running.
- [ ] **Step 5:** Memory check: open a 500+ page PDF (create one with `TestPdf`-style code in a scratch script, or merge `KillerPDF.pdf` 10 times), scroll top to bottom fast, read RSS with `ps -o rss= -p <pid>`; record the number in the task report.
- [ ] **Step 6:** `dotnet test src/KillerPDF.Avalonia.Tests` still passes. Commit: `git commit -m "Add Avalonia window with page view, zoom and page list"`.

---

### Task 6: Editing commands, save prompts, drag and drop

**Files:**
- Modify: `src/KillerPDF.Avalonia/Views/MainWindow.axaml`, `MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `MainWindow.Session`, `SelectedPages`, `OpenFile`, `RefreshDocument` (Task 5); `PdfSession` edit API (Task 2).

Behavior to build:

1. Toolbar buttons, in this order after Open: Save, Merge… | Rotate left, Rotate right | Delete, Move up, Move down | zoom group (Task 5) | Undo, Redo. Buttons are disabled when no document is open; Undo/Redo follow `CanUndo`/`CanRedo`; Move up is disabled for page 0, Move down for the last page; Move works on the single first selected page.
2. Rotate left = `Rotate(SelectedPages, -90)`, right = `+90`. With no selection, use the current visible page.
3. Delete asks for confirmation ("Delete N page(s)?"). `InvalidOperationException` from deleting every page shows a message.
4. Merge… opens a multi-select file picker for `.pdf` (`StorageProvider.OpenFilePickerAsync`) and calls `Session.Merge`.
5. Save: `Session.Save()`. Save As (Cmd+Shift+S): `StorageProvider.SaveFilePickerAsync`, then `SaveAs`. On exception: message with the error; state stays as is.
6. Unsaved changes: on window closing and before `OpenFile` replaces a dirty session, show Save / Don't Save / Cancel. Cancel keeps the window and document.
7. Keys: Cmd+S, Cmd+Shift+S, Cmd+Z undo, Cmd+Shift+Z redo, Backspace or Delete deletes selected pages (only when the page list has focus).
8. Drag and drop: dropping one PDF with no document open, or without Option held, opens it (after the unsaved prompt). Dropping one or more PDFs with Option (`KeyModifiers.Alt`) held while a document is open merges them.
9. Every edit wraps in try/catch and shows the error message; the session is unchanged on failure (guaranteed by `PdfSession.Apply`).

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** `dotnet build` → 0 errors; `dotnet test src/KillerPDF.Avalonia.Tests` → pass.
- [ ] **Step 3:** Manual run on a copy of `KillerPDF.pdf` in `/tmp`: rotate page 2, delete page 3, move page 1 down, merge a second copy, undo twice, redo once, Save As `/tmp/kp-edited.pdf`. Then verify outside the app: `dotnet run --project src/KillerPDF.Avalonia -- --render-folder /tmp/kp-edit-check --out /tmp/kp-edit-check.csv` on a folder that holds only `kp-edited.pdf`, and check page count and the swapped width/height of the rotated page in the CSV. Take screenshots.
- [ ] **Step 4:** Commit: `git commit -m "Add page edit commands, save prompts and drag and drop"`.

---

### Task 7: macOS packaging

**Files:**
- Create: `src/KillerPDF.Avalonia/packaging/Info.plist`
- Create: `src/KillerPDF.Avalonia/package-mac.sh`

**Interfaces:**
- Produces: `src/KillerPDF.Avalonia/package-mac.sh` writes `artifacts/mac/KillerPDF.app` and `artifacts/mac/KillerPDF-<version>-osx-arm64.dmg` under the repo root, and prints both sizes. Add `artifacts/` only if `.gitignore` does not already ignore it — do not edit `.gitignore`; instead write into `src/KillerPDF.Avalonia/bin/mac/` if `artifacts/` is not ignored.

- [ ] **Step 1: Info.plist**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>KillerPDF</string>
  <key>CFBundleDisplayName</key><string>KillerPDF</string>
  <key>CFBundleIdentifier</key><string>net.killerpdf.mac.prototype</string>
  <key>CFBundleExecutable</key><string>KillerPDF</string>
  <key>CFBundleIconFile</key><string>KillerPDF.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>__VERSION__</string>
  <key>CFBundleVersion</key><string>__VERSION__</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSArchitecturePriority</key><array><string>arm64</string></array>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>PDF document</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key><array><string>com.adobe.pdf</string></array>
    </dict>
  </array>
</dict>
</plist>
```

- [ ] **Step 2: package-mac.sh**

```bash
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/KillerPDF.csproj" | head -1)"
out="$root/artifacts/mac"
app="$out/KillerPDF.app"
rm -rf "$out" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$here/KillerPDF.Avalonia.csproj" -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -o "$out/publish"
cp -R "$out/publish/." "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$here/packaging/Info.plist" > "$app/Contents/Info.plist"
iconset="$out/KillerPDF.iconset" && mkdir -p "$iconset"
for s in 16 32 128 256 512; do
  sips -z $s $s "$root/Resources/kp-icon.png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$root/Resources/kp-icon.png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/KillerPDF.icns"
codesign --force --deep --sign - "$app"
dmg="$out/KillerPDF-$version-osx-arm64.dmg"
hdiutil create -volname KillerPDF -srcfolder "$app" -ov -format UDZO "$dmg" >/dev/null
du -sh "$app" "$dmg"
```

Check first: `git check-ignore -q artifacts/x && echo ignored`. If not ignored, change `out` to `"$here/bin/mac"` (bin is ignored).

- [ ] **Step 3: Run and verify**

Run: `chmod +x src/KillerPDF.Avalonia/package-mac.sh && src/KillerPDF.Avalonia/package-mac.sh`
Then: `codesign --verify --verbose "$app"`; `open "$app"` starts the window; `/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:LSHandlerRank' "$app/Contents/Info.plist"` prints `Alternate`.
Then confirm the default PDF app is unchanged: `swift -e 'import AppKit; print(NSWorkspace.shared.urlForApplication(toOpen: URL(fileURLWithPath: "'$PWD'/KillerPDF.pdf"))!.path)'` prints a path that is not KillerPDF.app (before and after `open "$app"`).
Then: `open -a "$app" KillerPDF.pdf` opens the file in the app (tests the Finder file-open path from Task 5).

- [ ] **Step 4: Commit**

```bash
git add src/KillerPDF.Avalonia/packaging src/KillerPDF.Avalonia/package-mac.sh
git commit -m "Add macOS app bundle and disk image script"
```

---

### Task 8: GitHub Actions on macOS arm64 (fork only)

**Files:**
- Create: `.github/workflows/mac-prototype.yml`

```yaml
name: mac-prototype
on:
  push:
    branches: [mac-avalonia-prototype]
  workflow_dispatch:
jobs:
  build:
    runs-on: macos-15
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: uname -m
      - run: dotnet test src/KillerPDF.Avalonia.Tests -c Release
      - run: dotnet test engine/KillerPdf.Engine.Tests/KillerPdf.Engine.Tests.csproj -c Release --logger "trx;LogFileName=engine.trx" || true
      - run: src/KillerPDF.Avalonia/package-mac.sh
      - uses: actions/upload-artifact@v4
        with:
          name: KillerPDF-osx-arm64
          path: |
            artifacts/mac/*.dmg
            src/KillerPDF.Avalonia/bin/mac/*.dmg
            **/engine.trx
          if-no-files-found: warn
```

The engine test step uses `|| true` because of the 6 known Windows-only test failures; the TRX file is kept as evidence.

- [ ] **Step 1:** Create the file. Check that other workflows in `.github/workflows/` do not run on this branch and fail for unrelated reasons; if they do, note it in the report and do not edit them.
- [ ] **Step 2:** Commit `git commit -m "Add macOS arm64 build workflow for the prototype"`, push to `fork`, watch with `gh run watch -R azwanzuharimi/KillerPDF`.
- [ ] **Step 3:** Expected: job green, `uname -m` prints `arm64`, artifact uploaded.

---

### Task 9: Measurements and #320 report draft

**Files:**
- Create: `src/KillerPDF.Avalonia/REPORT.md`

- [ ] **Step 1: Collect numbers on this Mac** (state the Mac model from `sysctl -n machdep.cpu.brand_string` and `sw_vers`):
  - `.app` and `.dmg` sizes (from Task 7 output).
  - Cold start: time from `open -a KillerPDF.app KillerPDF.pdf` to first page visible (stopwatch or a startup log line written by the app when the first page bitmap is drawn; if you add such a line, guard it behind env var `KP_TRACE=1`).
  - Batch render of `KillerPDF.pdf` at scale 1.5: total ms, median and p95 ms per page from the CSV.
  - Corpus: clone `https://github.com/SteveTheKiller/KillerPDF-Corpus` shallowly into `/tmp`, run `--render-folder` over its PDF collections (skip anything over 2 GB total), report files, pages, failures, failure types.
  - RSS memory from Task 5 step 5.
  - Engine tests on macOS arm64: 4456 passed, 6 failed, with the 6 causes (4 line ending, 1 Windows path, 1 backslash as path separator).
- [ ] **Step 2: Write REPORT.md** in plain words, for the maintainer: what was built, numbers in tables, what works, what does not (notarization, OCR, print, annotations, forms), rendering differences found against the Windows app if any were seen, and a link to the fork branch and CI run. No marketing tone.
- [ ] **Step 3: Commit** `git commit -m "Add macOS prototype measurements and report draft"` and push.
