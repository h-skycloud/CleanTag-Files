// Copyright (c) CleanTag contributors. Licensed under the MIT License.

using CleanTag.Core;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using IO = System.IO;

namespace Files.App.UserControls.CleanTag;

public sealed partial class CleanTagAnalysisView : UserControl, IDisposable
{
	private readonly IFileTagsService tags = Ioc.Default.GetRequiredService<IFileTagsService>();
	private readonly IFileTagsSettingsService tagSettings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
	private readonly IStorageService storage = Ioc.Default.GetRequiredService<IStorageService>();
	private CancellationTokenSource? scanCancellation;
	private StorageAnalysisSnapshot? snapshot;
	private int folder, selected = -1, visibleCount = 200, generation;
	private bool scanning, disposed;
	public event Action? CloseRequested;
	public event Action<string>? BrowseRequested;

	public CleanTagAnalysisView()
	{
		InitializeComponent();
		Map.Selected += SelectNode;
		Map.EnterFolder += ShowFolder;
		Map.Hovered += text => Details.Text = text;
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
			await ScanAsync();
		}
		catch (Exception ex) { ReportError(ex); }
	}

	private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
	private async Task ScanAsync()
	{
		if (scanning || disposed || string.IsNullOrWhiteSpace(RootPath.Text)) return;
		int version = ++generation;
		scanning = true;
		scanCancellation?.Dispose();
		scanCancellation = new();
		ChooseButton.IsEnabled = ScanButton.IsEnabled = RootPath.IsEnabled = false;
		StopButton.IsEnabled = true;
		Items.IsEnabled = Map.IsHitTestVisible = UpButton.IsEnabled = TagButton.IsEnabled = BrowseButton.IsEnabled = false;
		Progress.Visibility = Visibility.Visible;
		Progress.IsIndeterminate = true;
		selected = -1;
		try
		{
			var progress = new Progress<StorageScanProgress>(p =>
			{
				if (!disposed && version == generation && scanning)
					Status.Text = $"{p.Files:N0} · {FileEntry.FormatSize(p.Bytes)} · {p.Elapsed.TotalSeconds:0.0}s";
			});
			var result = await new StandardStorageScanner().ScanAsync(RootPath.Text, new(), progress, scanCancellation.Token);
			if (disposed || version != generation) return;
			snapshot = result;
			scanning = false;
			ShowFolder(0);
			string state = result.Status switch
			{
				StorageScanStatus.Cancelled => Strings.CleanTagPartial.GetLocalizedResource(),
				StorageScanStatus.Failed => Strings.CleanTagError.GetLocalizedResource(),
				_ => Strings.CleanTagComplete.GetLocalizedResource()
			};
			Status.Text = $"{state} · {result.Nodes[0].Files:N0} · {FileEntry.FormatSize(result.Nodes[0].Bytes)} · {result.IssueCount:N0} {Strings.CleanTagWarnings.GetLocalizedResource()}";
		}
		catch (Exception ex) { ReportError(ex); }
		finally
		{
			scanning = false;
			if (!disposed)
			{
				ChooseButton.IsEnabled = ScanButton.IsEnabled = RootPath.IsEnabled = true;
				Items.IsEnabled = Map.IsHitTestVisible = true;
				StopButton.IsEnabled = Progress.IsIndeterminate = false;
				Progress.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void ShowFolder(int id)
	{
		if (snapshot is null || scanning || disposed || !snapshot.Nodes[id].IsDirectory) return;
		folder = id;
		visibleCount = 200;
		selected = id;
		TagButton.IsEnabled = true;
		BrowseButton.IsEnabled = true;
		UpButton.IsEnabled = id > 0;
		RefreshRows();
		Map.SetSnapshot(snapshot, id, null);
		Details.Text = snapshot.GetPath(id);
	}

	private void RefreshRows()
	{
		if (snapshot is null) return;
		var children = snapshot.Children(folder).OrderByDescending(id => snapshot.Nodes[id].Bytes).ToArray();
		Items.Items.Clear();
		foreach (int id in children.Take(visibleCount))
		{
			var node = snapshot.Nodes[id];
			Items.Items.Add(new ListViewItem { Tag = id, Content = $"{(node.IsDirectory ? "▸ " : "")} {node.Name}   {FileEntry.FormatSize(node.Bytes)}" });
		}
		MoreButton.Visibility = children.Length > visibleCount ? Visibility.Visible : Visibility.Collapsed;
	}

	private void SelectNode(int id)
	{
		if (snapshot is null || scanning || disposed) return;
		selected = id;
		Map.SelectNode(id);
		Details.Text = snapshot.GetPath(id);
		TagButton.IsEnabled = true;
		BrowseButton.IsEnabled = true;
	}
	private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Items.SelectedItem is ListViewItem { Tag: int id }) SelectNode(id);
	}
	private void Items_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
	{
		if (Items.SelectedItem is ListViewItem { Tag: int id } && snapshot?.Nodes[id].IsDirectory == true) ShowFolder(id);
	}
	private void Items_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
	{
		if (e.Key == Windows.System.VirtualKey.Enter && Items.SelectedItem is ListViewItem { Tag: int id } && snapshot?.Nodes[id].IsDirectory == true)
		{
			ShowFolder(id);
			e.Handled = true;
		}
	}
	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		if (snapshot is null || scanning) return;
		int id = selected >= 0 ? selected : folder;
		if (!snapshot.Nodes[id].IsDirectory) id = snapshot.Nodes[id].Parent;
		BrowseRequested?.Invoke(snapshot.GetPath(id));
	}
	private void More_Click(object sender, RoutedEventArgs e) { visibleCount += 200; RefreshRows(); }
	private void Up_Click(object sender, RoutedEventArgs e) { if (snapshot is not null && folder > 0) ShowFolder(snapshot.Nodes[folder].Parent); }
	private void Stop_Click(object sender, RoutedEventArgs e) => scanCancellation?.Cancel();
	private void Back_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
	private void View_Unloaded(object sender, RoutedEventArgs e) => Dispose();

	private async void Tags_Click(object sender, RoutedEventArgs e)
	{
		if (snapshot is null || selected < 0 || scanning || disposed) return;
		string path = snapshot.GetPath(selected);
		bool isDirectory = snapshot.Nodes[selected].IsDirectory;
		try
		{
			FileSafety.CheckAncestors(isDirectory ? path : IO.Path.GetDirectoryName(path)!);
			if ((IO.File.GetAttributes(path) & IO.FileAttributes.ReparsePoint) != 0) throw new IO.IOException(Strings.CleanTagError.GetLocalizedResource());
			var identity = FileTagsHelper.GetFileFRN(path);
			var modified = IO.File.GetLastWriteTimeUtc(path);
			var current = FileTagsHelper.ReadFileTag(path);
			var choices = tagSettings.FileTagList.ToArray();
			var panel = new StackPanel { Spacing = 8 };
			var boxes = choices.Select(tag => new CheckBox { Content = tag.Name, IsChecked = current.Contains(tag.Uid) }).ToArray();
			foreach (var box in boxes) panel.Children.Add(box);
			var newTag = new TextBox { PlaceholderText = Strings.CleanTagNewTag.GetLocalizedResource() };
			panel.Children.Add(newTag);
			var dialog = new ContentDialog
			{
				XamlRoot = XamlRoot,
				Title = Strings.CleanTagTags.GetLocalizedResource(),
				Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
				PrimaryButtonText = Strings.CleanTagSave.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource()
			};
			if (await dialog.ShowAsync() != ContentDialogResult.Primary || disposed) return;
			FileSafety.CheckAncestors(isDirectory ? path : IO.Path.GetDirectoryName(path)!);
			if ((IO.File.GetAttributes(path) & IO.FileAttributes.ReparsePoint) != 0 ||
				FileTagsHelper.GetFileFRN(path) != identity || IO.File.GetLastWriteTimeUtc(path) != modified)
				throw new IO.IOException(Strings.CleanTagChanged.GetLocalizedResource());
			var ids = choices.Where((tag, index) => boxes[index].IsChecked == true).Select(tag => tag.Uid)
				.Concat(current.Where(id => !choices.Any(tag => tag.Uid == id))).ToList();
			if (!string.IsNullOrWhiteSpace(newTag.Text))
			{
				string name = newTag.Text.Trim();
				if (!tagSettings.GetTagsByName(name).Any()) tagSettings.CreateNewTag(name, "#0078D4");
				ids.AddRange(tagSettings.GetTagsByName(name).Select(tag => tag.Uid));
			}
			var storable = await storage.TryGetStorableAsync(path);
			if (storable is null) throw new IO.FileNotFoundException();
			string[] updated = ids.Distinct().ToArray();
			if (await tags.SetFileTagAsync(storable, updated))
			{
				FileTagsHelper.GetDbInstance().SetTags(path, FileTagsHelper.GetFileFRN(path), updated);
				Status.Text = Strings.CleanTagSaved.GetLocalizedResource();
			}
			else Status.Text = Strings.CleanTagError.GetLocalizedResource();
		}
		catch (Exception ex) { ReportError(ex); }
	}
	private void ReportError(Exception ex)
	{
		App.Logger.LogError(ex, "CleanTag analysis view failed");
		if (!disposed) Status.Text = Strings.CleanTagError.GetLocalizedResource() + ": " + ex.Message;
	}
	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		generation++;
		scanCancellation?.Cancel();
		scanCancellation?.Dispose();
	}
}
