// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Files.Shared.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using IO = System.IO;

namespace Files.App.UserControls.CleanTag;

public sealed partial class CleanTagFolderContents : UserControl, IDisposable
{
	public ObservableCollection<CleanTagFolderEntry> Items { get; } = [];
	private readonly CancellationTokenSource cancellation = new();
	private bool disposed;

	public CleanTagFolderContents()
	{
		InitializeComponent();
		SetLayout(ApplicationData.Current.LocalSettings.Values.TryGetValue("CleanTag.Review.FolderList", out var value) && value is true);
	}

	public async Task LoadAsync(string path)
	{
		var token = cancellation.Token;
		var snapshot = await Task.Run(() =>
		{
			var result = new List<CleanTagFolderEntry>();
			foreach (var child in new IO.DirectoryInfo(path).EnumerateFileSystemInfos())
			{
				token.ThrowIfCancellationRequested();
				try
				{
					if ((child.Attributes & (IO.FileAttributes.ReparsePoint | IO.FileAttributes.Offline | IO.FileAttributes.System)) != 0) continue;
					bool folder = (child.Attributes & IO.FileAttributes.Directory) != 0;
					result.Add(new(child.FullName, child.Name, folder, folder ? 0 : ((IO.FileInfo)child).Length, child.LastWriteTime));
					if (result.Count == 101) break;
				}
				catch (IO.IOException) { }
				catch (UnauthorizedAccessException) { }
			}
			return result;
		}, token);
		if (disposed) return;
		foreach (var entry in snapshot.Take(100).OrderByDescending(entry => entry.IsFolder).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
			Items.Add(entry);
		Summary.Text = string.Format(Strings.CleanTagFolderSummary.GetLocalizedResource(), Items.Count(entry => entry.IsFolder), Items.Count(entry => !entry.IsFolder));
		Scope.Text = (snapshot.Count > 100 ? Strings.CleanTagFolderLimited : Strings.CleanTagFolderScope).GetLocalizedResource();
		Empty.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		// A single loader bounds shell work and never delays review actions.
		_ = LoadThumbnailsAsync(token);
	}

	private async Task LoadThumbnailsAsync(CancellationToken token)
	{
		foreach (var entry in Items)
		{
			if (token.IsCancellationRequested) return;
			try
			{
				// Recheck before touching content: a child may have changed since enumeration.
				var attributes = IO.File.GetAttributes(entry.Path);
				if ((attributes & (IO.FileAttributes.ReparsePoint | IO.FileAttributes.Offline | IO.FileAttributes.System | (IO.FileAttributes)0x00400000)) != 0) continue;
				var options = !entry.IsFolder && FileExtensionHelpers.IsImagePreviewFile(IO.Path.GetExtension(entry.Path)) ? IconOptions.None : IconOptions.ReturnIconOnly;
				var bytes = await FileThumbnailHelper.GetIconAsync(entry.Path, 96, entry.IsFolder, options);
				if (token.IsCancellationRequested) return;
				if (bytes is null) continue;
				var image = await bytes.ToBitmapAsync();
				if (token.IsCancellationRequested) return;
				entry.Thumbnail = image;
			}
			catch (Exception) { /* Keep the native fallback glyph if a preview is unavailable. */ }
		}
	}

	private void Grid_Click(object sender, RoutedEventArgs e) { SetLayout(false); GridButton.Focus(FocusState.Programmatic); }
	private void List_Click(object sender, RoutedEventArgs e) { SetLayout(true); ListButton.Focus(FocusState.Programmatic); }
	private void SetLayout(bool list)
	{
		Rows.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
		Tiles.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
		GridButton.IsChecked = !list;
		ListButton.IsChecked = list;
		ApplicationData.Current.LocalSettings.Values["CleanTag.Review.FolderList"] = list;
	}
	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		cancellation.Cancel();
		foreach (var entry in Items) entry.Thumbnail = null;
	}
}

public sealed class CleanTagFolderEntry : ObservableObject
{
	public string Path { get; }
	public string Name { get; }
	public bool IsFolder { get; }
	public string Details { get; }
	public string Modified { get; }
	public string Tooltip => $"{Name}\n{Details} · {Modified}";
	public string Glyph => IsFolder ? "\uE8B7" : "\uE8A5";
	private ImageSource? thumbnail;
	public Visibility GlyphVisibility => thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
	public ImageSource? Thumbnail
	{
		get => thumbnail;
		set { if (SetProperty(ref thumbnail, value)) OnPropertyChanged(nameof(GlyphVisibility)); }
	}
	public CleanTagFolderEntry(string path, string name, bool folder, long size, DateTime modified)
	{
		Path = path; Name = name; IsFolder = folder;
		Details = folder ? Strings.Folder.GetLocalizedResource() : $"{IO.Path.GetExtension(name).TrimStart('.').ToUpperInvariant()} · {FileEntry.FormatSize(size)}";
		Modified = modified.ToString("g");
	}
}
