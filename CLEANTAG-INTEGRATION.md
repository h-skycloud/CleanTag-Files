# CleanTag experimental integration

Based on Files Community commit `6b721e819e4006670c5926325450fef9157aa6b4`.
This is an experimental migration branch, not a replacement for released CleanTag.

## Included

- Native disk analysis from the Files Home page, in the same window.
- CleanTag's read-only hierarchical scanner, cancellation and bounded Win2D map.
- Folder drill-down, parent navigation, paged size-sorted entries and keyboard
  Enter navigation. Zero-byte entries remain available in the list.
- Explicit file/folder tag changes through the existing Files services. A new tag
  can be created in the same dialog; unchecking known tags removes assignments.
- Return to the Files browser for preview, properties and normal file operations.
- Independent development package identity, protocol and settings/index storage.
- No CleanTag catalog connection, automatic legacy import or initialized Sentry.

Files tag UIDs are stored in NTFS alternate data streams (`:files`) and indexed
by Files. The alternate streams belong to the files, not the application package.
Changing a real file's tags can therefore affect another Files installation.
The separate package identity does not isolate those file metadata writes.

## Build and run

Use Windows with Visual Studio C++ and Windows application development components,
.NET 10 and Developer Mode. From this checkout:

```powershell
./build-cleantag.ps1 -Register
dotnet run --project tests/CleanTag.Analysis.Checks/CleanTag.Analysis.Checks.csproj -p:Platform=x64
```

Open **CleanTag Experimental** from Start. Select **Disk analysis** on Home, then
choose a test folder. Double-click a folder or press Enter to drill down. Select
a file or folder and use **Files tags** to edit its tags.

The build script restores the out-of-process COM server explicitly and constructs
a complete development layout before registration, including image assets and
the server executable. Registering the raw compiler output without those assets
caused an early native `0x80070003` startup failure.

The compiler toolset is pinned to Roslyn 5.9, matching upstream source generators.
Do not remove it while using a Visual Studio compiler older than that version.

## Verification

- x64 Debug build and development package registration succeeded.
- Ten analysis checks passed: totals, hierarchy, extensions, treemap area and byte
  conservation, hit testing, non-mutating filters, cancellation, missing roots and
  absence of catalog/sidecar writes.
- Startup was verified after fixing the incomplete development asset layout.
- Interactive scan verified with a disposable fixture: three files, one nested
  folder, 4,121 logical bytes and zero warnings; the zero-byte file stays listed.
- Folder drill-down and parent navigation worked without rescanning.
- Created a new Files tag on the test text file, reused it on the test folder,
  and verified matching `:files` UIDs on both. Both tags appeared in the normal
  Files browser after navigating from analysis.
- Final native window remained open and responsive. These checks were on the
  local development package, not a signed public release or clean machine.
- UI deletion was not exercised; this integration does not add a deletion path.

## Remaining migration work

The original CleanTag inbox/review workflow, legacy catalog import, advanced
analysis filtering, full product branding and public distribution are not part of
this experiment yet. Files already owns the browser, preview, tag management,
properties and recycle workflows; subsequent work should use those facilities.

Retain upstream `LICENSE-MIT` and `LICENSE-MPL`. The extracted analysis module
retains its CleanTag MIT license in `src/CleanTag.Analysis/LICENSE`.
Do not commit local logs, crash dumps, user catalogs, package settings or certificates.
