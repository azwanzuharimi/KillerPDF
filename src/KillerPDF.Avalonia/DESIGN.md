# KillerPDF macOS prototype (Avalonia) — design

Date: 2026-10-07. Branch: `mac-avalonia-prototype`, based on `dev/1.9-overkill` (v1.9.0).

## Purpose

1. Give evidence for [discussion #320](https://github.com/SteveTheKiller/KillerPDF/discussions/320): run KillerPdf.Engine rendering, an Avalonia shell and macOS packaging on Apple Silicon (arm64).
2. Give a daily PDF viewer and light editor on macOS that never becomes the default PDF app.

## Scope

In scope: open, render, zoom, scroll, page list, toolbar, one theme (Dark), rotate, delete, reorder, merge, save, undo, `.app` + `.dmg` for arm64, corpus batch render.

Out of scope: OCR, print, annotations, forms, signatures, passwords, other themes, translations, Windows/Linux builds, Apple notarization.

## Constraints

- No change to any file outside `src/KillerPDF.Avalonia/` and `src/KillerPDF.Avalonia.Tests/`, except one CI workflow file in the fork. The WPF csproj already excludes `src\**`.
- No linked files from `Services/`. `PdfEngineIntegration.cs` uses `System.Drawing`, which fails on macOS.
- Do not change the version in `KillerPDF.csproj` or the engine csproj. The engine build fails if they differ.
- `Info.plist` sets `LSHandlerRank` to `Alternate` for PDF. The app must never become the default PDF app.

## Architecture

```
src/KillerPDF.Avalonia/
  App, MainWindow (Avalonia UI)
        |
  PdfSession          <- no UI code, unit tested
    bytes in memory, undo/redo stacks of byte[]
    rotate / delete / move / merge via PdfIncrementalPageEditor
    Save writes temp file in target folder, then File.Move over target
        |
  PageRasterizer      <- PdfPageRenderer -> Avalonia WriteableBitmap (BGRA8888)
  MacFontResolver     <- IPdfFontResolver for /System/Library/Fonts and /Library/Fonts
        |
  engine/KillerPdf.Engine (ProjectReference)
```

### PdfSession

- `Open(path)`: read bytes, open `PdfDocument`, read `PdfPageInformation`.
- `Rotate(indices, degrees)`, `Delete(indices)`, `Move(from, to)`, `Merge(paths)`: push current bytes to undo stack, build new bytes with `PdfIncrementalPageEditor`, reopen. Clear redo.
- `Undo()`, `Redo()`, `IsDirty`, `PageCount`, `Pages`, `Document`.
- `Save()` / `SaveAs(path)`: write to a temp file in the same folder, then `File.Move(overwrite: true)`. The original file does not change before Save.
- Delete of all pages is rejected.
- Undo depth limit: 50 steps.

### Rendering

- Render only pages in or near the viewport, on background tasks, with cancellation on scroll and zoom.
- Render size = page size in points × zoom × screen scale.
- Thumbnails: small renders, cached per document version.
- A page that throws shows a placeholder with the error text. Other pages continue.

### MacFontResolver

- Maps Arial, Times New Roman, Courier New and their bold/italic faces to files in `/System/Library/Fonts/Supplementary/`.
- Falls back to a family name search in `/System/Library/Fonts`, `/Library/Fonts`, `~/Library/Fonts`.
- Returns `null` when no match is found; the engine then uses its own fallback.

## UI

```
| Open Save Merge | rotate L/R | Delete Up Down | - zoom + Fit | Undo Redo |
| page list (thumbnails, multi select) | pages in one scrolling column     |
| status: file name, page n / N, unsaved marker                           |
```

Keys: Cmd+O, Cmd+S, Cmd+Shift+S, Cmd+Z, Cmd+Shift+Z, Cmd+Plus, Cmd+Minus, Cmd+0, Backspace (delete selected pages). Drag and drop of PDF files opens them (one file) or merges them (when a document is open, with Option held).

Colors come from `Themes/Dark.xaml`, copied by value.

## Errors

| Case | Behavior |
|---|---|
| Damaged or encrypted PDF | Message dialog; app stays open |
| Quit or open another file with unsaved changes | Ask: Save, Don't Save, Cancel |
| Save fails | Show error; in-memory document and original file stay intact |
| Page render throws | Placeholder for that page only |

## Testing and evidence

- `src/KillerPDF.Avalonia.Tests` (xUnit): PdfSession open, each edit, undo/redo, original unchanged before Save, Save output reopens with expected page count, order and rotation; MacFontResolver finds Arial.
- Headless command: `KillerPDF --render-folder <dir> --out <csv>` renders every page of every PDF, writes file, page, ms, error.
- Engine test suite on macOS arm64. Baseline at start: 4456 pass, 6 fail (Windows line endings and paths in tests).
- GitHub Actions job on `macos-15` (arm64) in the fork: build, test, package, upload `.dmg`.

## Packaging

`src/KillerPDF.Avalonia/package-mac.sh`: `dotnet publish -r osx-arm64 --self-contained`, build `KillerPDF.app` with `Info.plist` and icon, ad hoc sign with `codesign -s -`, build `.dmg` with `hdiutil`.

## Report for #320

Numbers: `.app` size, `.dmg` size, cold start time, open time, render ms per page, memory, corpus pass rate. States what is not done (notarization, OCR, print). The user posts it.
