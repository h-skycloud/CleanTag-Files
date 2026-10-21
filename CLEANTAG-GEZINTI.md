# Gezinti

Home → **Gezinti** → choose a folder → **1 — Files** or **2 — Folders**.
The selected folder's immediate children are reviewed one at a time, sorted by
name. Subfolders are separate review items in folder mode; file mode does not
silently traverse their contents.

- **Keep** saves the decision and advances. Unchanged kept items are omitted the
  next time this folder/mode is opened.
- **Later** saves the decision and advances. **Review later items** opens the
  deferred queue for the selected folder and mode, including after restart.
- **Recycle** asks for confirmation and uses Files' native shell operation to
  move the current item to the Recycle Bin. Folder confirmation explicitly covers
  all contents. Cancellation or a failed operation leaves the current item in the
  queue. There is no permanent-delete fallback in Gezinti.
- **Files tags** edits the current item's existing Files tags or creates a tag.
  Tagging does not advance the review, so the same item can then be kept, deferred
  or recycled.
- **Browse in Files** opens the folder in the ordinary Files browser.

**Review settings** provides persistent arrow-key, recycle-confirmation and sort
preferences. Arrow keys default to enabled: **Left = Recycle**, **Right = Keep**,
**Down = Later**. Confirmation defaults to enabled. If confirmation is explicitly
disabled, Left recycles immediately. The Recycle Bin-only behavior is unchanged.
The active shortcut mapping is shown above the decision buttons.

Gezinti receives decision arrows before Files hotkeys, including when its contents
list, image, document or view buttons have focus. Editable inputs, mode/settings
controls, media controls, dialogs and modified key combinations retain their normal
keyboard behavior. Holding an arrow does not process repeated items. Clicking
**Use arrow keys to decide** enables the preference and restores decision focus.
The Files pointer workaround no longer moves focus out of Gezinti.

File previews reuse Files image, media, text, shell-handler and basic controls.
PDF and PDF-compatible AI documents use the Windows PDF renderer, one page at a
time, with explicit paging buttons. PSD/PSB embedded JPEG resources and binary EPS
TIFF previews are read with bounded lengths; Windows thumbnails and registered
handlers provide fallbacks. A design file without a compatible embedded preview
or provider displays an explicit unavailable message. ZIP, 7z, RAR, TAR, GZ and
other supported archive formats list up to 300 entry names and sizes using Files
7-Zip backend. Nothing is extracted. Encrypted headers, damaged archives and
unsupported formats display a failure message. The same document/archive preview
is available in the ordinary Files preview pane. Document loads are cancellable
and do not disable review decisions; recycling waits for preview handles to close. Folder previews offer thumbnail and details layouts, with the last
layout remembered. They show up to 100 available immediate children, folders
first, with names, type/size, modified dates and native icons. Image thumbnails
load one at a time without blocking decisions. A visible scope note makes clear
that review actions apply to the whole current folder; the preview does not
recursively scan or total its contents. Offline, system and reparse children are
excluded. Preview work is canceled and resources released when advancing.

Decisions are stored in the development package's private local data as
`cleantag-review.json`. A file lock merges decisions across concurrent windows;
atomic replacement protects the previous state if saving fails. Decisions match
the selected root, item type and file revision/identity. Changed or replaced items
are reviewed again.

Recycling checks the root boundary, current attributes, revision and native file
identity before execution and again in the shell pre-delete callback. Reparse,
system and offline entries are excluded from the review listing. A shell request
that cannot use the Recycle Bin is rejected. Recovery is through the Windows
Recycle Bin.

Verified on 2026-10-19: 20 automated checks; native file/folder review, Keep/Later,
deferred items after restart, tagging in place, canceling deletion, and approved
recycling of disposable file and folder fixtures. Both Recycle Bin payloads were
verified, including the folder's child file.

Settings/keyboard verification: the native UI exercised Right/Keep, Down/Later,
Left opening the confirmation dialog followed by cancellation, disabled shortcuts,
normal arrow behavior in the path textbox and dialog controls, size sorting, and
preference restoration after restarting the application. Final preferences were
restored to arrow keys enabled, confirmation enabled and name sorting.

Folder UI verification: native thumbnail/details switching, real image thumbnails
from generated test images, folder/file icons, sizes and modified dates, empty
folders, and a 105-file fixture with a visible 100-item limit were exercised.
The details layout was restored after restarting. The earlier contents-list navigation behavior has been superseded by decision
arrows when review shortcuts are enabled.

The earlier folder-view key guards were superseded by decision arrows taking
precedence in review content when shortcuts are enabled. Use the visible decision
focus button to enable shortcuts again after turning them off in settings.

Verified on 2026-10-22: 26 automated analysis/preview checks and a successful
x64 Debug packaged build/launch. Native UI verification covered PDF and
PDF-compatible AI first/second pages, PSD embedded images, ZIP entry names and
sizes, a corrupt ZIP failure message, and the same PDF/PSD/ZIP content in the
ordinary Files preview pane. Archive paths, including a parent-relative test name,
were only displayed; no entries were extracted.

Right/Keep worked from document page controls. Down/Later worked from the archive
entry list. Left opened deletion confirmation; Down in that dialog did not
advance the queue, and Escape preserved the item. Right worked after cancellation.
Disabled shortcuts did not advance; the decision-focus button re-enabled them.
Left in the root-path textbox retained editing behavior. No actual deletion was
performed during this verification. The existing confirmation-off and size-sort
preferences were restored, with decision shortcuts enabled.
