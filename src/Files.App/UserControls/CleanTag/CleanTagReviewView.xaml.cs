// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Files.Shared.Helpers;
using Files.App.ViewModels.Previews;
using Files.App.UserControls.FilePreviews;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
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
	private CleanTagFolderContents? folderContents;
	private CleanTagDocumentPreview? documentPreview;
	private string root = "";
	private bool busy, disposed;
	private bool arrowKeysEnabled = true, confirmDelete = true, laterOnly;
	private int sortOrder;
	private int generation;
	private CancellationTokenSource lifetime = new();
	public event Action? CloseRequested;
	public event Action<string>? BrowseRequested;
	public CleanTagReviewView()
	{
		InitializeComponent();
		PreviewKeyDown += Review_PreviewKeyDown;
		Inspector.BusyChanged += SetBusy;
		Inspector.Failed += Error;
		var preferences = ApplicationData.Current.LocalSettings.Values;
		if (preferences.TryGetValue("CleanTag.Review.ArrowKeys", out var keys) && keys is bool enabled) arrowKeysEnabled = enabled;
		if (preferences.TryGetValue("CleanTag.Review.ConfirmDelete", out var confirm) && confirm is bool ask) confirmDelete = ask;
		if (preferences.TryGetValue("CleanTag.Review.Sort", out var order) && order is int index && index is >= 0 and <= 2) sortOrder = index;
		UpdateShortcutHint();
	}
	private async void Review_PreviewKeyDown(object sender, KeyRoutedEventArgs e) => await TryHandleDecisionKeyAsync(e);
	private void DecisionFocus_Click(object sender, RoutedEventArgs e)
	{
		arrowKeysEnabled = true;
		ApplicationData.Current.LocalSettings.Values["CleanTag.Review.ArrowKeys"] = true;
		UpdateShortcutHint();
		Focus(FocusState.Programmatic);
	}

	internal async Task<bool> TryHandleDecisionKeyAsync(KeyRoutedEventArgs e)
	{
		if (e.Handled || disposed || !arrowKeysEnabled || session?.Current is null || !UIHelpers.CanShowDialog) return false;
		ReviewDecision? decision = e.Key switch
		{
			VirtualKey.Left => ReviewDecision.Recycled,
			VirtualKey.Right => ReviewDecision.Keep,
			VirtualKey.Down => ReviewDecision.Later,
			_ => null
		};
		if (decision is null || HotKeyHelpers.GetCurrentKeyModifiers() != KeyModifiers.None) return false;
		if (NeedsOwnArrowKeys(e.OriginalSource as DependencyObject) || NeedsOwnArrowKeys(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)) return false;
		e.Handled = true;
		if (!busy && !e.KeyStatus.WasKeyDown) await DecideAsync(decision.Value);
		return true;
	}
	private bool NeedsOwnArrowKeys(DependencyObject? element)
	{
		while (element is not null && element != this)
		{
			if (element is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or ComboBox or Slider or RadioButton or CheckBox or
				ToggleSwitch or MediaPlayerElement) return true;
			element = VisualTreeHelper.GetParent(element);
		}
		return false;
	}
	private void UpdateShortcutHint() => ShortcutHint.Visibility = arrowKeysEnabled ? Visibility.Visible : Visibility.Collapsed;
	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		if (busy || disposed) return;
		SetBusy(true);
		bool reorder = false;
		try
		{
			var arrows = new ToggleSwitch { Header = Strings.CleanTagReviewArrowKeys.GetLocalizedResource(), IsOn = arrowKeysEnabled };
			var confirmation = new ToggleSwitch { Header = Strings.CleanTagReviewAskDelete.GetLocalizedResource(), IsOn = confirmDelete };
			var sorting = new ComboBox { Header = Strings.CleanTagReviewSort.GetLocalizedResource(), HorizontalAlignment = HorizontalAlignment.Stretch,
				ItemsSource = new[] { Strings.CleanTagReviewSortName.GetLocalizedResource(), Strings.CleanTagReviewSortSize.GetLocalizedResource(), Strings.CleanTagReviewSortDate.GetLocalizedResource() }, SelectedIndex = sortOrder };
			var panel = new StackPanel { Spacing = 16 };
			panel.Children.Add(arrows);
			panel.Children.Add(new TextBlock { Text = Strings.CleanTagReviewShortcuts.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap });
			panel.Children.Add(confirmation);
			panel.Children.Add(new TextBlock { Text = Strings.CleanTagReviewRecycleInfo.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap });
			panel.Children.Add(sorting);
			var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = Strings.CleanTagReviewSettings.GetLocalizedResource(),
				Content = new ScrollViewer { Content = panel, MaxHeight = 420 }, PrimaryButtonText = Strings.CleanTagSave.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource(), DefaultButton = ContentDialogButton.Close };
			if (await dialog.ShowAsync() != ContentDialogResult.Primary || disposed) return;
			var preferences = ApplicationData.Current.LocalSettings.Values;
			preferences["CleanTag.Review.ArrowKeys"] = arrows.IsOn;
			preferences["CleanTag.Review.ConfirmDelete"] = confirmation.IsOn;
			preferences["CleanTag.Review.Sort"] = sorting.SelectedIndex;
			arrowKeysEnabled = arrows.IsOn;
			confirmDelete = confirmation.IsOn;
			reorder = sortOrder != sorting.SelectedIndex;
			sortOrder = sorting.SelectedIndex;
			UpdateShortcutHint();
		}
		catch (Exception ex) { Error(ex); }
		finally { if (!disposed) SetBusy(false); }
		if (reorder && !disposed && session is not null) await StartAsync(laterOnly);
	}

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
	private async Task StartAsync(bool deferredOnly)
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
			laterOnly = deferredOnly;
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
				return (sortOrder switch
				{
					1 => result.OrderByDescending(item => item.Size).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
					2 => result.OrderByDescending(item => item.ModifiedTicks).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
					_ => result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
				}).ToArray();
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
			var reviewRoot = root;
			Inspector.SetTarget(item.FullPath, item.IsDirectory,
				() => ReviewSafety.Validate(reviewRoot, item, FileTagsHelper.GetFileFRN(item.FullPath)?.ToString()));
			if (item.IsDirectory)
			{
				var contents = new CleanTagFolderContents();
				folderContents = contents;
				Preview.Content = contents;
				await contents.LoadAsync(item.FullPath);
			}
			else if (CleanTagDocumentPreview.Supports(item.Extension))
			{
				var document = new CleanTagDocumentPreview { ReleaseOnUnload = false };
				documentPreview = document;
				Preview.Content = document;
				_ = document.LoadAsync(item.FullPath);
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
		catch (OperationCanceledException) when (disposed) { }
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
				if (confirmDelete)
				{
				var confirmation = new ContentDialog { XamlRoot = XamlRoot,
					Title = Strings.CleanTagReviewDelete.GetLocalizedResource(),
					Content = string.Format((item.IsDirectory ? Strings.CleanTagReviewDeleteFolder : Strings.CleanTagReviewDeleteFile).GetLocalizedResource(), item.FullPath),
					PrimaryButtonText = Strings.CleanTagReviewDelete.GetLocalizedResource(), CloseButtonText = Strings.Cancel.GetLocalizedResource(),
					DefaultButton = ContentDialogButton.Close };
				if (await confirmation.ShowAsync() != ContentDialogResult.Primary || disposed) return;
				}
				if (documentPreview is not null) await documentPreview.ReleaseAsync();
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
		Inspector.SetReviewBusy(value);
		ChooseButton.IsEnabled = RefreshButton.IsEnabled = DeferredButton.IsEnabled = SettingsButton.IsEnabled = RootPath.IsEnabled = Mode.IsEnabled = !value;
		Decisions.IsEnabled = !value && session?.Current is not null;
		if (!value && session?.Current is not null) Focus(FocusState.Programmatic);
	}
	private void Message(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
	{
		StatusLine.Text = message;
		Status.Message = message;
		Status.Severity = severity;
		Status.IsOpen = severity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
	}
	private void Error(Exception ex) { App.Logger.LogError(ex, "CleanTag review failed"); if (!disposed) Message(Strings.CleanTagError.GetLocalizedResource() + ": " + ex.Message, InfoBarSeverity.Error); }
	private void ClearPreview() { Inspector.SetTarget(null, false); documentPreview?.Dispose(); documentPreview = null; folderContents?.Dispose(); folderContents = null; Preview.Content = null; previewModel?.PreviewControlBase_Unloaded(null, new()); previewModel = null; }
	private void Back_Click(object sender, RoutedEventArgs e) { if (!busy) CloseRequested?.Invoke(); }
	private void View_Unloaded(object sender, RoutedEventArgs e) => Dispose();
	public void Dispose() { if (disposed) return; disposed = true; generation++; lifetime.Cancel(); ClearPreview(); Inspector.Dispose(); }
}
