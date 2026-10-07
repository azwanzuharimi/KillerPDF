# KillerPDF on macOS: prototype report (for discussion #320)

Date: 2026-10-07, updated 2026-10-08. Draft for the maintainer.

## Summary

- **What this is:** a small macOS app on top of `KillerPdf.Engine`. It opens, renders and edits PDF pages on Apple Silicon.
- **No native PDF library.** The engine's own renderer draws every page.
- **Branch:** https://github.com/azwanzuharimi/KillerPDF/tree/mac-avalonia-prototype (based on `dev/1.9-overkill`, v1.9.0)
- **CI run (macOS arm64):** https://github.com/azwanzuharimi/KillerPDF/actions/runs/37603352769 (CI: the automatic build and test on GitHub)

Key results:

| Area | Result |
|---|---|
| Engine tests on macOS | 4456 passed, 6 failed (of 4462). All 6 failures come from Windows line endings or Windows paths in the tests. |
| Prototype tests | 42 passed, 0 failed (local run) |
| Render speed, `KillerPDF.pdf` | Median 11 ms per page |
| Corpus | 6147 files from 3 corpus zips. No crash and no hang. |
| Cold start | Median 718 ms (until the window shows) |
| Size | `KillerPDF.app` 173 MB, `.dmg` 73 MB |

## What I built

- All new code is in `src/KillerPDF.Avalonia/` and `src/KillerPDF.Avalonia.Tests/`, plus one workflow file.
- No engine file, no WPF file and no version number changed.

### Stack

| Part | Choice |
|---|---|
| UI | Avalonia 12.1.3 (Fluent theme, colors copied from `Themes/Dark.xaml`) |
| Runtime | .NET 10 (SDK 10.0.400), self contained, `osx-arm64` |
| Rendering | `KillerPdf.Engine` `PdfPageRenderer` into an Avalonia `WriteableBitmap` |
| Editing | `PdfIncrementalPageEditor` (same code path as the Windows app) |
| Fonts | Small resolver. It maps PDF font names to files in `/System/Library/Fonts`. |
| Native PDF libraries | None (no PDFKit, no PDFium) |
| Shared Windows code | Only `Services/PdfFontStyle.cs` is linked. `PdfEngineIntegration.cs` is not linked. It uses `System.Drawing`, which does not work on macOS. |

"Self contained" means the app includes the .NET runtime.

### Features

Unit tests or headless window tests check these features. "Headless" means the test runs with no visible window.

- Open a PDF. A damaged or encrypted file shows a message. The app stays open.
- Pages show in one scrolling column, with a page list of thumbnails.
- Pages render in the background. Old render jobs stop on scroll and zoom.
- Zoom in, zoom out, fit.
- Rotate, delete, move up and down, merge other PDFs.
- The app refuses to delete every page.
- Undo and redo (up to 50 steps).
- Save and Save As. The app writes a temporary file in the same folder. Then it moves that file over the target.
- If save fails, the open document and the original file do not change.
- Ask to save on quit (window close and Cmd+Q). Also ask when another file opens with unsaved changes.
- Pages with `/Rotate 90` or `270` render landscape, not stretched.
- Headless command `--render-folder`. It writes one CSV row per page.

## How to try it

- Build `KillerPDF.app` and the `.dmg` with `src/KillerPDF.Avalonia/package-mac.sh` (see "How to reproduce").
- The app is signed ad hoc only (`codesign -s -`). Ad hoc: a local signature with no Apple identity.
- It is not notarized (not sent to Apple for its automatic security check). So Gatekeeper blocks a downloaded copy. The user must allow it in System Settings.
- `Info.plist` declares PDF support with `LSHandlerRank` = `Alternate`.
- The default PDF app on this Mac stayed the same (another PDF app). It did not change after install, or after I opened and used the app.
- The app never asks to become the default.

## Results

### Test machine

| Item | Value |
|---|---|
| Mac | Mac16,7, Apple M4 Pro, 48 GB memory |
| macOS | 27.0.1 (build 26A434) |
| Date of runs | 2026-10-07 and 2026-10-08 |

### Tests

| Suite | Result | Where |
|---|---|---|
| Prototype tests (`KillerPDF.Avalonia.Tests`) | 42 passed, 0 failed | Local run (the CI run above had 37, before the last fixes) |
| Engine tests (`KillerPdf.Engine.Tests`) on macOS arm64 | 4456 passed, 6 failed | Local run and CI run above (runner macos-15 (arm64), job time 3 min 59 s) |

The 6 engine test failures are test issues, not engine issues:

| Count | Cause |
|---|---|
| 4 | Test expects Windows line endings (CRLF). macOS writes LF. |
| 1 | Test expects a Windows path (`C:\input\...`) |
| 1 | Test expects `..\` (backslash) as the path separator |

### Render speed: `KillerPDF.pdf` (50 pages, scale 1.5)

- Headless batch render with the packaged binary, 3 runs.
- Time is per page in milliseconds. I measured only the engine render call.
- p95: 95% of pages render in this time or less.
- Process wall time: the real clock time of the whole process.

| Run | Sum of page times | Median | p95 | Max | Process wall time |
|---|---|---|---|---|---|
| 1 | 767 ms | 11 ms | 39 ms | 122 ms | 0.84 s |
| 2 | 736 ms | 11 ms | 38 ms | 132 ms | 0.81 s |
| 3 | 765 ms | 11 ms | 37 ms | 126 ms | 0.84 s |

The slowest page is always page 1. It probably includes first use costs (run time compile, font load).

### Corpus: KillerPDF-Corpus release v1.8.1

A corpus is a large set of test PDF files.

- Corpus repo commit `54ecf22f4eb3cb5b8bace6f7a68e57ee01c42b27`, release `v1.8.1`.
- The full regression set is about 12.5 GB in 10 zip parts.
- I took three zips that fit in about 1 GB:
  - the standards set,
  - regression part 10 of 10,
  - the fuzz set (files damaged on purpose).
- I checked each zip against its published SHA-256 (a file checksum).

Two kinds of failure:

| Term | Meaning | What the app shows |
|---|---|---|
| File failure | The engine could not open the file. | A message |
| Page failure | The file opened, but one page threw an error. | A placeholder for that page |

| Collection | Files | Files that failed to open | Pages | Pages that failed | Files with a failed page | Median ms/page | p95 ms/page | Max ms/page | Wall time |
|---|---|---|---|---|---|---|---|---|---|
| Standards | 552 | 41 (7.4%) | 6764 | 26 (0.4%) | 7 | 2 | 38 | 865 | 65 s |
| Regression part 10 | 5515 | 132 (2.4%) | 31102 | 40 (0.1%) | 40 | 0 | 8 | 1423 | 88 s |
| Fuzz (damaged on purpose) | 80 | 61 (76%) | 117 | 3 | 3 | 0 | 47 | 142 | 5.1 s (80 processes) |
| Total | 6147 | 234 | 37983 | 69 | 50 | | | | |

Notes:

- Times are whole milliseconds. A median of 0 means most pages render in under 1 ms. Many corpus files are small test files.
- No file caused a process crash or a hang.
- Fuzz files ran one process per file with a 30 s limit. None reached the limit.
- Fuzz files have a `.fuzz` extension. I copied each one to a `.pdf` name, because `--render-folder` picks only `*.pdf`.
- Peak memory of the batch process was 1.6 GB (standards) and 1.4 GB (regression part 10), from `/usr/bin/time -l`.
- This is the batch command, not the app window. See the memory table for the app.
- I did not run the same files through the Windows app. I do not know if the Windows build fails on the same files.

Top reasons a file failed to open (standards and regression part 10). Each row groups files by exact message, except the last row:

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

### Memory

RSS (resident memory): the part of the process memory that is in RAM now.

| Case | RSS | How measured |
|---|---|---|
| App window idle after open, `KillerPDF.pdf` (50 pages) | 293 MB | Real app window |
| App window idle after open, generated 520 page file | 249 MB | Real app window |
| Fast scroll through 520 pages, peak | 355 MB | Headless window test |
| Same, after scroll stops | 350 MB | Headless window test |
| Page bitmaps kept at one time | 6 or fewer | Only pages in or near the view keep a bitmap |
| Zoom 500% on a Retina screen | Not measured | One page bitmap at this size is about 194 MB, so a few pages can pass 1 GB |

### Size

| Item | Size | Note |
|---|---|---|
| `KillerPDF.app` | 173 MB | Self contained, includes the .NET runtime |
| `KillerPDF-1.9.0-osx-arm64.dmg` | 73 MB | Compressed disk image |

### Cold start and open time

Cold start: the app starts when it is not already running.

| Item | Value |
|---|---|
| Cold start (`open -a KillerPDF.app KillerPDF.pdf` until the window with title `KillerPDF.pdf` exists) | Median 718 ms, min 683 ms, max 783 ms (5 runs: 718, 692, 683, 739, 783) |
| Cold start until the first page pixels show | Not measured |
| Open `KillerPDF.pdf` (`PdfSession.Open`), first open in a new process | 17–24 ms (5 processes) |
| Open plus render of page 1 at scale 1.5, first open in a new process | 157–175 ms (5 processes) |
| Same file opened again in the same process: open / open plus page 1 | 2 ms / 54–56 ms |

How I measured the cold start:

- Quit the app. Start `open`.
- Ask System Events (AppleScript) for the window name about every 50 ms.
- Each AppleScript call also takes some time. So the true step is a little larger than 50 ms.
- The time stops when the window with the document title exists. It does not wait for the first page to draw.
- The app files were in the disk cache, because I built the app just before.

How I measured the last three rows:

- A small console program, not committed.
- It calls `PdfSession.Open` and `PageRasterizer.Render(0, 1.5)` with a `Stopwatch`, twice in each process.
- It does not include process start or window creation.

## What I checked in the real window

- App: the built `KillerPDF.app`, on 2026-10-08.
- I drove the app with AppleScript (keys, menus, dialog buttons).
- For Finder drag and drop I used real mouse events (CGEvent).
- I took screenshots.
- The first checks ran on the build before the app name fix (commit 9f4ec788).
- I ran these checks on the fixed build (commit 3b061681).

| Check | Result | Build |
|---|---|---|
| Open with `open -a KillerPDF.app test.pdf` (the same macOS launch path as Finder Open With) | The file opens. Pages are sharp on the Retina screen. Thumbnails show. | 9f4ec788 |
| Rotate right | The page turns. The unsaved marker and Undo update. | 9f4ec788 |
| Cmd+Q with unsaved changes, then Cancel | The prompt "Save changes to test.pdf?" shows Save, Don't Save and Cancel. Cancel keeps the app open and keeps the edit. The file on disk does not change. | 9f4ec788 |
| Cmd+Q with no changes | The app quits with no prompt. | 9f4ec788 |
| Cmd+Shift+S | The native macOS save panel opens. The saved file has 50 pages. Page 1 is 842 x 595 (turned), checked with `--render-folder`. | 9f4ec788 |
| Merge | The native open panel lets the user select only PDF files. The page count goes from 50 to 51. | 9f4ec788 |
| Finder drag with Option held | The dropped file merges, 51 to 52 pages. | 9f4ec788 |
| Finder drag without Option, with unsaved changes | The save prompt shows. Don't Save opens the dropped file. The old file does not change. | 9f4ec788 |
| Cmd+Q with unsaved changes, then Cancel | The prompt shows. Cancel keeps the app open and keeps the edit. | 3b061681 |
| A second Cmd+Q while the prompt is open | No second prompt opens. | 3b061681 |
| Cmd+Q, then Don't Save | The app quits. The file on disk does not change (same checksum). | 3b061681 |
| Cmd+Q with no changes | The app quits. The next cold start then ran. | 3b061681 |
| Menu bar name | The menu bar reads "KillerPDF", with the Apple menu and the KillerPDF menu. | 3b061681 |

## What is not done

- Apple notarization (needs a paid Apple Developer account). The app is signed ad hoc only.
- OCR.
- Print.
- Annotations, forms, signatures, password prompt.
- Themes other than Dark.
- Translations.
- Windows and Linux builds of this Avalonia app.
- Intel Mac (`osx-x64`) build.
- Save writes a new file and renames it over the old one. Because of this:
  - the app does not keep file permissions, Finder tags and extended attributes,
  - a symlink (a link to another file) becomes a normal file.

## Findings for the maintainer

1. **Engine tests on macOS.**
   - 6 of 4462 tests fail.
   - They fail only because they expect Windows line endings or Windows paths.
   - I did not change any engine test.
2. **Merge fails when form field names clash.**
   - Merge two PDFs whose form fields have the same name. The engine throws `NotSupportedException: Merged AcroForms must have unique field names.`
   - Source: `engine/KillerPdf.Engine/Editing/PdfIncrementalPageEditor.cs`, lines 5754–5755.
   - The Windows app uses the same `AddImportedDocument` path. So I expect the same error there (not tested on Windows).
   - The prototype shows the message and keeps the document unchanged.
3. **Crash at start while the display sleeps.**
   - Every launch while the Mac display was asleep failed with `Avalonia.Native was not able to start the RenderTimer` (error -6661).
   - I saw this in two separate test sessions.
   - With the display awake, the app starts.
   - The error comes from Avalonia's macOS layer, not from the engine.
4. **Memory at high zoom on Retina.**
   - The app draws each page bitmap at full screen resolution.
   - At 500% zoom on a Retina screen, one page is about 194 MB. So memory can pass 1 GB.
   - Not measured.
   - A possible fix: at high zoom, render only the visible part of a page (tiles).
5. **Corpus.**
   - No crash and no hang over 6147 files.
   - 234 files did not open:

   | Group | Files |
   |---|---|
   | Fuzz files (damaged on purpose) | 61 |
   | Encrypted standards and regression files (12 + 65). The prototype has no password prompt. | 77 |
   | Standards and regression files with structure errors (29 + 67) | 96 |

6. **Wrong app name in the menu bar (fixed).**
   - The macOS menu bar showed "Avalonia Application". The app menu had "About Avalonia".
   - `Info.plist` sets `CFBundleName` to `KillerPDF`.
   - Avalonia takes the menu bar name from `Application.Name`, not from `Info.plist`.
   - The fix sets `Name="KillerPDF"` in `App.axaml`.
   - The fix also adds an empty `NativeMenu`. So Avalonia does not add its own "About Avalonia" item.
   - The menu bar now shows "KillerPDF".
   - The app menu has Services, Hide KillerPDF, Hide Others, Show All and Quit.
   - Two small issues remain. Both are cosmetic:
     - The app menu now starts with an empty separator line.
     - Its Quit item reads only "Quit", not "Quit KillerPDF".

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

CSV output:

- Columns: `file,page,width,height,ms,error`.
- Page numbers start at 0.
- Page `-1` means the file did not open.
