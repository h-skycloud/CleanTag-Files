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

Gezinti receives these arrows before Files' general hotkeys. Editable text,
selection controls, media controls, dialogs and modified key combinations retain
their normal keyboard behavior. Holding an arrow does not process repeated items.
After loading an item or completing an action, keyboard focus returns to Gezinti.

File previews reuse Files' image, media, text, shell-handler and basic preview
controls. Folder previews show up to 100 immediate child names without recursively
scanning the folder. Preview resources are released when advancing or recycling.

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
