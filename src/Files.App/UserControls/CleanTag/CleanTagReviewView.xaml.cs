// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Files.Shared.Helpers;
using Files.App.ViewModels.Previews;
using Files.App.UserControls.FilePreviews;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using IO = System.IO;

namespace Files.App.UserControls.CleanTag;

public sealed partial class CleanTagReviewView : UserControl, IDisposable
{
	private readonly ReviewDecisionStore store = new(IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "cleantag-review.json"));
	private List<ReviewRecord> records = [];
	private ReviewSession? session;
	private BasePreviewModel? previewModel;
	private string root = "";
	private bool busy, disposed;
	private int generation;
	private CancellationTokenSource lifetime = new();
	public event Action? CloseRequested;
	public event Action<string>? BrowseRequested;
	public CleanTagReviewView() => InitializeComponent();

	private async void Choose_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var picker = new FolderPicker();
			WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);
			picker.FileTypeFilter.Add("*");
			var result = await picker.PickSingleFolderAsync();
			if (result is null || disposed) return;
			RootPath.Text = result.Path;
			Mode.SelectedIndex = -1;
			session = null;
			ClearPreview();
			ItemTitle.Text = ItemDetails.Text = "";
			Decisions.IsEnabled = false;
			Message(Strings.CleanTagReviewMode.GetLocalizedResource());
		}
		catch (Exception ex) { Error(ex); }
	}
	private async void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Mode.SelectedIndex >= 0 && !string.IsNullOrWhiteSpace(RootPath.Text)) await StartAsync(false);
	}
	private async void Start_Click(object sender, RoutedEventArgs e) => await StartAsync(false);
	private async void Deferred_Click(object sender, RoutedEventArgs e) => await StartAsync(true);
	private async Task StartAsync(bool laterOnly)
	{
		if (busy || disposed) return;
		if (Mode.SelectedIndex < 0 || string.IsNullOrWhiteSpace(RootPath.Text))
		{
			Message(Strings.CleanTagReviewIntro.GetLocalizedResource()); return;
		}
		SetBusy(true);
		int version = ++generation;
		ClearPreview();
		session = null;
		try
		{
			root = IO.Path.TrimEndingDirectorySeparator(IO.Path.GetFullPath(RootPath.Text));
			bool folders = Mode.SelectedIndex == 1;
			Message(Strings.CleanTagReviewLoading.GetLocalizedResource());
			records = await store.LoadAsync(lifetime.Token);
			var entries = await Task.Run(() =>
			{
				FileSafety.CheckAncestors(root);
				var result = new List<FileEntry>();
				foreach (var item in new IO.DirectoryInfo(root).EnumerateFileSystemInfos())
				{
					lifetime.Token.ThrowIfCancellationRequested();
					if (((item.Attributes & IO.FileAttributes.Directory) != 0) != folders) continue;
					if ((item.Attributes & (IO.FileAttributes.ReparsePoint | IO.FileAttributes.System | IO.FileAttributes.Offline)) != 0) continue;
					result.Add(new(item.FullName, folders ? 0 : ((IO.FileInfo)item).Length, item.LastWriteTimeUtc.Ticks)
					{ IsDirectory = folders, NativeId = FileTagsHelper.GetFileFRN(item.FullName)?.ToString() });
				}
				return result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
			}, lifetime.Token);
			if (disposed || generation != version) return;
			session = new(root, entries, records, laterOnly);
			await ShowCurrentAsync();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { Error(ex); }
		finally { if (!disposed) SetBusy(false); }
	}
	private async Task ShowCurrentAsync()
	{
		if (disposed) return;
		ClearPreview();
		if (session?.Current is not { } item)
		{
			ItemTitle.Text = Strings.CleanTagReviewDone.GetLocalizedResource();
			ItemDetails.Text = "";
			Message(Strings.CleanTagReviewDoneHint.GetLocalizedResource());
			return;
		}
		ItemTitle.Text = item.Name;
		ItemDetails.Text = $"{item.FullPath}\n{(item.IsDirectory ? Strings.Folder.GetLocalizedResource() : FileEntry.FormatSize(item.Size))} · {item.ModifiedLabel}";
		Message(string.Format(Strings.CleanTagReviewRemaining.GetLocalizedResource(), session.Remaining));
		int version = generation;
		try
		{
			ReviewSafety.Validate(root, item, FileTagsHelper.GetFileFRN(item.FullPath)?.ToString());
			if (item.IsDirectory)
			{
				var names = await Task.Run(() => new IO.DirectoryInfo(item.FullPath).EnumerateFileSystemInfos().Take(101)
					.Select(child => child.Name + ((child.Attributes & IO.FileAttributes.Directory) != 0 ? " /" : "")).ToArray(), lifetime.Token);
				if (disposed || version != generation) return;
				var panel = new StackPanel { Spacing = 6 };
				if (names.Length == 0) panel.Children.Add(new TextBlock { Text = Strings.CleanTagReviewEmptyFolder.GetLocalizedResource() });
				foreach (string name in names.Take(100)) panel.Children.Add(new TextBlock { Text = name });
				if (names.Length > 100) panel.Children.Add(new TextBlock { Text = Strings.CleanTagReviewMore.GetLocalizedResource() });
				Preview.Content = new ScrollViewer { Content = panel };
			}
			else
			{
				var listed = new ListedItem { ItemPath = item.FullPath, ItemNameRaw = item.Name, FileExtension = item.Extension,
					FileSizeBytes = item.Size, PrimaryItemAttribute = StorageItemTypes.File };
				listed.ItemFile = await StorageFileExtensions.DangerousGetFileFromPathAsync(item.FullPath);
				BasePreviewModel model;
				Func<UserControl> control;
				if (FileExtensionHelpers.IsImagePreviewFile(item.Extension))
				{
					var image = new ImagePreviewViewModel(listed); model = image; control = () => new ImagePreview(image);
				}
				else if (FileExtensionHelpers.IsAudioFile(item.Extension) || FileExtensionHelpers.IsVideoFile(item.Extension))
				{
					var media = new MediaPreviewViewModel(listed); model = media; control = () => new MediaPreview(media);
				}
				else if (FileExtensionHelpers.IsTextFile(item.Extension) || CodePreviewViewModel.IsCodeFile(item.Extension))
				{
					var text = new TextPreviewViewModel(listed); model = text; control = () => new TextPreview(text);
				}
				else if (!FileExtensionHelpers.IsExecutableFile(item.Extension) && ShellPreviewViewModel.FindPreviewHandlerFor(item.Extension, 0) is not null)
				{
					var shell = new ShellPreviewViewModel(listed); model = shell; control = () => new ShellPreview(shell);
				}
				else { var basic = new BasicPreviewViewModel(listed); model = basic; control = () => new BasicPreview(basic); }
				previewModel = model;
				await model.LoadPreviewAndDetailsAsync();
				if (disposed || version != generation) { model.PreviewControlBase_Unloaded(null, new()); return; }
				Preview.Content = control();
			}
		}
		catch (Exception ex) { Error(ex); }
	}
	private async Task DecideAsync(ReviewDecision decision)
	{
		if (busy || disposed || session?.Current is not { } item) return;
		SetBusy(true);
		bool recycled = false;
		bool advanced = false;
		try
		{
			ReviewSafety.Validate(root, item, FileTagsHelper.GetFileFRN(item.FullPath)?.ToString());
			if (decision == ReviewDecision.Recycled)
			{
				var confirmation = new ContentDialog { XamlRoot = XamlRoot,
					Title = Strings.CleanTagReviewDelete.GetLocalizedResource(),
					Content = string.Format((item.IsDirectory ? Strings.CleanTagReviewDeleteFolder : Strings.CleanTagReviewDeleteFile).GetLocalizedResource(), item.FullPath),
					PrimaryButtonText = Strings.CleanTagReviewDelete.GetLocalizedResource(), CloseButtonText = Strings.Cancel.GetLocalizedResource(),
					DefaultButton = ContentDialogButton.Close };
				if (await confirmation.ShowAsync() != ContentDialogResult.Primary || disposed) return;
				ClearPreview();
				recycled = await FileOperationsHelpers.RecycleReviewItemAsync(root, item, MainWindow.Instance.WindowHandle);
				if (!recycled) throw new IO.IOException(Strings.CleanTagReviewRecycleFailed.GetLocalizedResource());
			}
			records = await store.RecordAsync(new(root, item, decision), lifetime.Token);
			session.Advance();
			advanced = true;
			generation++;
			await ShowCurrentAsync();
		}
		catch (Exception ex)
		{
			if (recycled && !advanced) { session.Advance(); generation++; await ShowCurrentAsync(); if (!disposed) Message(Strings.CleanTagReviewRecycledLogFailed.GetLocalizedResource(), InfoBarSeverity.Warning); }
			else Error(ex);
		}
		finally { if (!disposed) SetBusy(false); }
	}
	private async void Keep_Click(object sender, RoutedEventArgs e) => await DecideAsync(ReviewDecision.Keep);
	private async void Later_Click(object sender, RoutedEventArgs e) => await DecideAsync(ReviewDecision.Later);
	private async void Delete_Click(object sender, RoutedEventArgs e) => await DecideAsync(ReviewDecision.Recycled);
	private async void Tags_Click(object sender, RoutedEventArgs e)
	{
		if (busy || disposed || session?.Current is not { } item) return;
		SetBusy(true);
		try
		{
			ReviewSafety.Validate(root, item, FileTagsHelper.GetFileFRN(item.FullPath)?.ToString());
			if (await CleanTagTagEditor.EditAsync(item.FullPath, item.IsDirectory, XamlRoot)) Message(Strings.CleanTagSaved.GetLocalizedResource());
		}
		catch (Exception ex) { Error(ex); }
		finally { if (!disposed) SetBusy(false); }
	}
	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		if (session?.Current is { } item) BrowseRequested?.Invoke(item.IsDirectory ? item.FullPath : root);
	}
	private void SetBusy(bool value)
	{
		busy = value;
		ChooseButton.IsEnabled = RefreshButton.IsEnabled = DeferredButton.IsEnabled = RootPath.IsEnabled = Mode.IsEnabled = !value;
		Decisions.IsEnabled = !value && session?.Current is not null;
	}
	private void Message(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) { Status.Message = message; Status.Severity = severity; }
	private void Error(Exception ex) { App.Logger.LogError(ex, "CleanTag review failed"); if (!disposed) Message(Strings.CleanTagError.GetLocalizedResource() + ": " + ex.Message, InfoBarSeverity.Error); }
	private void ClearPreview() { Preview.Content = null; previewModel?.PreviewControlBase_Unloaded(null, new()); previewModel = null; }
	private void Back_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
	private void View_Unloaded(object sender, RoutedEventArgs e) => Dispose();
	public void Dispose() { if (disposed) return; disposed = true; generation++; lifetime.Cancel(); ClearPreview(); }
}
