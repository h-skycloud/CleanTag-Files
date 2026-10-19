// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Files.Shared.Helpers;
using System.IO;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.App.Utils.Storage;

public sealed partial class FileOperationsHelpers
{
	public static Task<bool> RecycleReviewItemAsync(string root, FileEntry entry, long ownerHwnd)
	{
		return STATask.Run(async () =>
		{
			ReviewSafety.Validate(root, entry, FileTagsHelper.GetFileFRN(entry.FullPath)?.ToString());
			using var op = new ShellFileOperations2();
			op.OwnerWindow = (HWND)(nint)ownerHwnd;
			op.Options = FILEOPERATION_FLAGS.FOF_SILENT | FILEOPERATION_FLAGS.FOF_NOCONFIRMATION |
				FILEOPERATION_FLAGS.FOF_NOERRORUI | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE | FILEOPERATION_FLAGS.FOF_ALLOWUNDO;
			bool deleted = false;
			var completion = new TaskCompletionSource<bool>();
			op.PreDeleteItem += (sender, args) =>
			{
				if ((args.Flags & _TRANSFER_SOURCE_FLAGS.TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0)
					throw new IOException("The Recycle Bin is unavailable.");
				if (string.Equals(args.SourceItem?.GetParsingPath(), entry.FullPath, StringComparison.OrdinalIgnoreCase))
					ReviewSafety.Validate(root, entry, FileTagsHelper.GetFileFRN(entry.FullPath)?.ToString());
			};
			op.PostDeleteItem += (sender, args) =>
			{
				deleted = args.Result.Succeeded;
				if (deleted) SafetyExtensions.IgnoreExceptions(() => UpdateFileTagsDb(args, "delete"), App.Logger);
			};
			op.FinishOperations += (sender, args) => completion.TrySetResult(args.Result.Succeeded);
			using var item = new ShellItem(entry.FullPath);
			op.QueueDeleteOperation(item);
			try { op.PerformOperations(); }
			catch { completion.TrySetResult(false); }
			return await completion.Task && deleted && !File.Exists(entry.FullPath) && !Directory.Exists(entry.FullPath);
		}, App.Logger);
	}
}
