# CleanTag Files

A Windows file manager with a persistent tagging workspace, previews, and a
file/folder review flow. This is the primary CleanTag edition, built from
[Files Community](https://github.com/files-community/Files). It is an independent
source fork, not an official Files release or a plugin loaded into stock Files.

The smaller [standalone CleanTag](https://github.com/h-skycloud/CleanTag) remains
available for its SQLite library and focused review workflow. The two editions
do not share catalogs or automatically synchronize their tags.

## Workspace

- Open **Gezinti** from Home, select a folder and choose files or folders.
- Keep a large preview and the tag inspector visible together. Search tags,
  create and assign a tag, or toggle assignments without advancing the queue.
- **Left = Recycle, Right = Keep, Down = Later**. Input fields, settings and
  dialogs retain their normal keyboard behavior. Use **Use arrow keys to decide**
  to enable shortcuts and restore decision focus.
- Confirmation can be enabled in review settings. Recycling never falls back to
  permanent deletion. Folder deletion includes the whole folder.
- Native Files browsing, tabs, properties, tag search and file operations.
- PDF / PDF-compatible AI pages, embedded PSD/PSB images, image/media/text previews,
  installed Windows preview providers and read-only archive contents.
- Disk analysis with a hierarchical scan and an interactive treemap.

Folder previews show up to 100 immediate items. Archive previews show up to 300
entries without extracting them. Design formats without a compatible embedded
image or installed Windows provider show an explicit unavailable message.

The tag inspector is visible in wide windows. Narrow windows retain the tag
dialog and native command overflow. Both light and dark system resources are used.

## Tags and backup

Files tag IDs are stored in NTFS alternate data streams on the original files.
Tag definitions and the application's index are separate metadata. Folder tags
apply to the folder itself; they do not propagate to its children. NTFS is
required for this tag backend. Cloud providers may not preserve alternate streams.

Before uninstalling or moving to another installation, use **Settings > Advanced >
Export settings**. That native ZIP includes the package-private tag index; copying
LocalState alone is insufficient. Restore it using **Import settings** after
installation. Review decisions are in the package's LocalState and should also
be backed up. Never include metadata exports in source commits or public releases.

CleanTag does not silently import or replace the standalone SQLite catalog.
Keep a backup of both editions when migrating.

## Build and distribution

Windows 10 2004 or newer, x64, Visual Studio 2026 with Windows/C++ development
components, and .NET 10 are required to build.

```powershell
./build-cleantag.ps1 -Configuration Release
dotnet run --project tests/CleanTag.Analysis.Checks/CleanTag.Analysis.Checks.csproj -p:Platform=x64
./tools/New-CleanTagFilesRelease.ps1
```

The current downloadable layout is **unsigned developer distribution**, not a
Store package or trusted signed MSIX. It requires Windows Developer Mode and the
Windows App SDK runtime used by the manifest. The installer only registers the
package; it does not enable Developer Mode, install certificates, change your
default file manager, or remove another Files installation.

Extract the entire archive, then run from its directory:

```powershell
./Install-CleanTagFiles.ps1
```

It verifies the release checksums and installs to a versioned per-user Programs
directory. Launch **CleanTag Files** from Start. To preserve existing metadata,
the legacy internal `CleanTag.Files.Experimental` package identity is retained;
the displayed product name is CleanTag Files. The internal Windows package
version follows the Files baseline and is distinct from the CleanTag release tag.

When a newer CleanTag release retains the same internal Windows package version,
Windows may keep the existing registered layout. The installer reports a
registration verification failure instead of claiming the new files are active.
Before replacing that layout, export settings and separately back up review
history, uninstall only **CleanTag Files**, install the new archive, then restore
the saved metadata. Uninstalling without a verified backup can lose the private
tag index. This developer distribution has no automatic in-place updater.

Public code signing and automatic updates are not configured. Do not describe
this build as a signed production installer. Logs, catalogs, settings, exports,
certificates and private keys are excluded from release inputs.

## Verification and upstream

See [integration notes](CLEANTAG-INTEGRATION.md), [review behavior](CLEANTAG-GEZINTI.md)
and the [original upstream README](README.upstream.md). The integration retains
Files history and should be maintained with upstream changes reviewed separately.

Preserve [MIT](LICENSE-MIT) and [MPL](LICENSE-MPL) notices and the license headers of
individual upstream files. CleanTag's extracted analysis module has its own MIT
notice in `src/CleanTag.Analysis/LICENSE`. This project is not affiliated with or
endorsed by Files Community.
