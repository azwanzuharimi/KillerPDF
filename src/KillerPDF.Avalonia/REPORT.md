# KillerPDF on macOS: prototype report (for discussion #320)

Date: 2026-10-07. Draft for the maintainer.

## Summary

I built a small macOS app on top of `KillerPdf.Engine`. It opens, renders and edits PDF pages on Apple Silicon. It uses no native PDF library: the engine's own renderer draws every page. The engine test suite runs on macOS with 6 failures out of 4462. All 6 come from Windows line endings or Windows paths in the tests.

- Branch: https://github.com/azwanzuharimi/KillerPDF/tree/mac-avalonia-prototype (based on `dev/1.9-overkill`, v1.9.0)
- CI run (macOS arm64): https://github.com/azwanzuharimi/KillerPDF/actions/runs/37603352769
- All new code is in `src/KillerPDF.Avalonia/` and `src/KillerPDF.Avalonia.Tests/`, plus one workflow file. No engine file, no WPF file and no version number changed.

## Stack

| Part | Choice |
|---|---|
| UI | Avalonia 12.1.3 (Fluent theme, colors copied from `Themes/Dark.xaml`) |
| Runtime | .NET 10 (SDK 10.0.400), self contained, `osx-arm64` |
| Rendering | `KillerPdf.Engine` `PdfPageRenderer` into an Avalonia `WriteableBitmap` |
| Editing | `PdfIncrementalPageEditor` (same code path as the Windows app) |
| Fonts | Small resolver that maps PDF font names to files in `/System/Library/Fonts` |
| Native PDF libraries | None (no PDFKit, no PDFium) |
| Shared Windows code | Only `Services/PdfFontStyle.cs` is linked. `PdfEngineIntegration.cs` is not linked, because it uses `System.Drawing`, which does not work on macOS. |

## Test machine

| Item | Value |
|---|---|
| Mac | Mac16,7, Apple M4 Pro, 48 GB memory |
| macOS | 27.0.1 (build 26A434) |
| Date of runs | 2026-10-07 |

## Numbers

### Package

| Item | Size | Note |
|---|---|---|
| `KillerPDF.app` | 173 MB | Self contained, includes the .NET runtime |
| `KillerPDF-1.9.0-osx-arm64.dmg` | 73 MB | Compressed disk image |

The app is signed ad hoc only (`codesign -s -`). It is not notarized, so Gatekeeper blocks a downloaded copy until the user allows it in System Settings.

`Info.plist` declares PDF support with `LSHandlerRank` = `Alternate`. After install, the default PDF app on this Mac stayed the same (another PDF app). The app never asks to become the default.

### Render speed: `KillerPDF.pdf` (50 pages, scale 1.5)

Headless batch render with the packaged binary, 3 runs. Time is per page in milliseconds, measured around the engine render call only.

| Run | Sum of page times | Median | p95 | Max | Process wall time |
|---|---|---|---|---|---|
| 1 | 767 ms | 11 ms | 39 ms | 122 ms | 0.84 s |
| 2 | 736 ms | 11 ms | 38 ms | 132 ms | 0.81 s |
| 3 | 765 ms | 11 ms | 37 ms | 126 ms | 0.84 s |

p95 means 95% of pages render in this time or less. The slowest page is always page 1. It probably includes first use costs (run time compile, font load).

### Corpus: KillerPDF-Corpus release v1.8.1

Corpus repo commit `54ecf22f4eb3cb5b8bace6f7a68e57ee01c42b27`, release `v1.8.1`. The full regression set is about 12.5 GB in 10 zip parts. I took three zips that fit in about 1 GB: the standards set, regression part 10 of 10, and the fuzz set (damaged on purpose). I checked each zip against its published SHA-256.

A "file failure" means the engine could not open the file. A "page failure" means the file opened but one page threw an error. The app shows a message for the first case and a placeholder for the second.

| Collection | Files | Files that failed to open | Pages | Pages that failed | Files with a failed page | Median ms/page | p95 ms/page | Max ms/page | Wall time |
|---|---|---|---|---|---|---|---|---|---|
| Standards | 552 | 41 (7.4%) | 6764 | 26 (0.4%) | 7 | 2 | 38 | 865 | 65 s |
| Regression part 10 | 5515 | 132 (2.4%) | 31102 | 40 (0.1%) | 40 | 0 | 8 | 1423 | 88 s |
| Fuzz (damaged on purpose) | 80 | 61 (76%) | 117 | 3 | 3 | 0 | 47 | 142 | 5.1 s (80 processes) |
| Total | 6147 | 234 | 37983 | 69 | 50 | | | | |

Notes:

- Times are whole milliseconds, so a median of 0 means most pages render in under 1 ms (many corpus files are small test files).
- No file caused a process crash or a hang. Fuzz files ran one process per file with a 30 s limit; none reached the limit.
- Fuzz files have a `.fuzz` extension. I copied each one to a `.pdf` name, because `--render-folder` picks only `*.pdf`.
- Peak memory of the batch process was 1.6 GB (standards) and 1.4 GB (regression part 10), from `/usr/bin/time -l`. This is the batch command, not the app window. See the memory table for the app.
- I did not run the same files through the Windows app. I do not know if the Windows build fails on the same files.

Top reasons a file failed to open (standards and regression part 10). Rows group files by exact message, except the last row:

| Standards | Regression part 10 | Error |
|---|---|---|
| 12 | 65 | `InvalidOperationException: Authenticate the document before rendering pages.` (encrypted; the prototype has no password prompt) |
| 9 | 23 | `InvalidOperationException: The catalog /Pages is not a page-tree dictionary.` |
| 6 | 9 | `InvalidOperationException: The document catalog is not a dictionary.` |
| 6 | 8 | `InvalidDataException: The PDF contains no pages.` |
| 2 | 9 | `PdfSyntaxException: The PDF contains no recoverable indirect objects (byte offset 0).` |
| 4 | 2 | `InvalidOperationException: The trailer /Root is not an indirect reference.` |
| 0 | 3 | `InvalidOperationException: The page tree references the same node more than once.` |
| 2 | 1 | `InvalidOperationException: The document catalog has no /Pages tree.` |
| 0 | 7 | `PdfSyntaxException: The PDF cross-reference data could not be rebuilt: ...` (all messages that start this way) |

Top reasons a page failed:

| Standards | Regression part 10 | Error |
|---|---|---|
| 13 | 3 | `FormatException: A rendering operand is not numeric.` |
| 4 | 0 | `FormatException: Image sample data has an invalid length.` |
| 3 | 0 | `PdfSyntaxException: Content recovery limit exceeded` |
| 0 | 6 | `FormatException: CID is outside the valid range.` |
| 0 | 5 | `PdfFilterException: LZW data contains an invalid dictionary code.` |
| 0 | 5 | `NotSupportedException: ... CMap /Adobe-Korea1-2 is not supported.` (predefined or inherited Korean encoding) |
| 0 | 4 | `FormatException: Overlapping or ambiguous ToUnicode code spaces.` |
| 0 | 3 | `FormatException: JPEG image metadata does not match its PDF dictionary.` |
| 0 | 2 | `OverflowException: Arithmetic operation resulted in an overflow.` |

### Memory (resident memory, RSS)

| Case | RSS | How measured |
|---|---|---|
| App window idle after open, `KillerPDF.pdf` (50 pages) | 293 MB | Real app window |
| App window idle after open, generated 520 page file | 249 MB | Real app window |
| Fast scroll through 520 pages, peak | 355 MB | Headless window test |
| Same, after scroll stops | 350 MB | Headless window test |
| Page bitmaps kept at one time | 6 or fewer | Only pages in or near the view keep a bitmap |
| Zoom 500% on a Retina screen | Not measured | One page bitmap at this size is about 194 MB, so a few pages can pass 1 GB |

### Start time

| Item | Value |
|---|---|
| Cold start (open the `.app` until the first page shows) | Not measured. The Mac screen was locked during this work, so I could not see or time the window. |
| Open time in the app window | Not measured (screen locked). |
| Open `KillerPDF.pdf` (`PdfSession.Open`), first open in a new process | 17–24 ms (5 processes) |
| Open plus render of page 1 at scale 1.5, first open in a new process | 157–175 ms (5 processes) |
| Same file opened again in the same process: open / open plus page 1 | 2 ms / 54–56 ms |

The last three rows come from a small console program that is not committed. It calls `PdfSession.Open` and `PageRasterizer.Render(0, 1.5)` with a `Stopwatch`, twice in each process. It does not include process start or window creation.

### Tests

| Suite | Result | Where |
|---|---|---|
| Prototype tests (`KillerPDF.Avalonia.Tests`) | 37 passed, 0 failed | Local run and CI run above |
| Engine tests (`KillerPdf.Engine.Tests`) on macOS arm64 | 4456 passed, 6 failed | Local run and CI run above (runner macos-15 (arm64), job time 3 min 59 s) |

The 6 engine test failures are test issues, not engine issues:

| Count | Cause |
|---|---|
| 4 | Test expects Windows line endings (CRLF); macOS writes LF |
| 1 | Test expects a Windows path (`C:\input\...`) |
| 1 | Test expects `..\` (backslash) as the path separator |

## What works

Checked by unit tests or headless window tests:

- Open a PDF. A damaged or encrypted file shows a message; the app stays open.
- Pages in one scrolling column, with a page list of thumbnails. Pages render in the background. Old render jobs stop on scroll and zoom.
- Zoom in, zoom out, fit.
- Rotate, delete, move up and down, merge other PDFs. Delete of every page is refused.
- Undo and redo (up to 50 steps).
- Save and Save As. The app writes a temporary file in the same folder, then moves it over the target. If save fails, the open document and the original file do not change.
- Ask to save on quit, or when another file opens with unsaved changes.
- Pages with `/Rotate 90` or `270` render landscape, not stretched.
- Headless command `--render-folder` that writes one CSV row per page.

Not checked by eye: the Mac screen was locked for all of this work. So I did not see the real native window, the Cmd key shortcuts, the native open and save dialogs, or Finder drag and drop with Option (merge). The code for these exists and the headless tests pass, but a person must try them on a real screen.

## What is not done

- Apple notarization (needs a paid Apple Developer account). The app is signed ad hoc only.
- OCR.
- Print.
- Annotations, forms, signatures, password prompt.
- Themes other than Dark.
- Translations.
- Windows and Linux builds of this Avalonia app.
- Intel Mac (`osx-x64`) build.

## Findings

1. **Engine tests on macOS.** 6 of 4462 tests fail only because they expect Windows line endings or Windows paths. I did not change any engine test.
2. **Merge fails when form field names clash.** Merging two PDFs whose form fields have the same name throws `NotSupportedException: Merged AcroForms must have unique field names.` (`engine/KillerPdf.Engine/Editing/PdfIncrementalPageEditor.cs`, lines 5754–5755). The Windows app uses the same `AddImportedDocument` path, so I expect the same error there (not tested on Windows). The prototype shows the message and keeps the document unchanged.
3. **Crash at start while the display sleeps.** Every launch while the Mac display was asleep failed with `Avalonia.Native was not able to start the RenderTimer` (error -6661). This was seen in two separate test sessions. With the display awake, the app starts. The error comes from Avalonia's macOS layer, not from the engine.
4. **Memory at high zoom on Retina.** Each page bitmap is drawn at full screen resolution. At 500% zoom on a Retina screen one page is about 194 MB, so memory can pass 1 GB. Not measured. A possible fix is to render only the visible part of a page (tiles) at high zoom.
5. **Corpus.** No crash and no hang over 6147 files. Of the 234 files that did not open: 61 are fuzz files (damaged on purpose), 77 are encrypted standards and regression files (12 + 65) that need a password prompt, which the prototype does not have, and 96 are standards and regression files with structure errors (29 + 67).

## How to reproduce

```sh
git clone -b mac-avalonia-prototype https://github.com/azwanzuharimi/KillerPDF
cd KillerPDF
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1

# tests
dotnet test src/KillerPDF.Avalonia.Tests -c Release
dotnet test engine/KillerPdf.Engine.Tests/KillerPdf.Engine.Tests.csproj -c Release

# build KillerPDF.app and the .dmg into artifacts/mac/
src/KillerPDF.Avalonia/package-mac.sh
du -sh artifacts/mac/KillerPDF.app artifacts/mac/*.dmg

# run the app
open artifacts/mac/KillerPDF.app

# headless render of every PDF in a folder (one CSV row per page)
BIN=artifacts/mac/KillerPDF.app/Contents/MacOS/KillerPDF
mkdir -p /tmp/kp && cp KillerPDF.pdf /tmp/kp/
$BIN --render-folder /tmp/kp --out /tmp/kp.csv --scale 1.5

# corpus: download a zip and its .sha256 from
# https://github.com/SteveTheKiller/KillerPDF-Corpus/releases/tag/v1.8.1
# (killerpdf-corpus-standards-v1.8.1.zip, ...-regression-v1.8.1-part10.zip, ...-fuzz-v1.8.1.zip),
# check it with shasum -a 256, unzip it, then:
/usr/bin/time -l $BIN --render-folder <unzipped folder> --out corpus.csv --scale 1.5
```

CSV columns: `file,page,width,height,ms,error`. Page numbers start at 0. Page `-1` means the file did not open.
