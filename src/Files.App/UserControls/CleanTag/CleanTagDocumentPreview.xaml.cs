// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Files.App.UserControls.FilePreviews;
using Files.App.ViewModels.Previews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SevenZip;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using IO = System.IO;

namespace Files.App.UserControls.CleanTag;

public sealed partial class CleanTagDocumentPreview : UserControl, IDisposable
{
	private readonly CancellationTokenSource cancellation = new();
	private readonly SemaphoreSlim renderGate = new(1, 1);
	private PdfDocument? pdf;
	private IRandomAccessStream? pdfStream;
	private BasePreviewModel? shellModel;
	private uint pageIndex;
	private bool disposed;
	private Task loadingTask = Task.CompletedTask;
	public bool ReleaseOnUnload { get; set; } = true;
	private const int ArchiveLimit = 300;
	public CleanTagDocumentPreview() => InitializeComponent();
	public static bool IsArchive(string extension) => extension.ToLowerInvariant() is ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".zst" or ".cab" or ".lzh" or ".jar" or ".mrpack";
	public static bool Supports(string extension) => IsArchive(extension) || extension.ToLowerInvariant() is ".pdf" or ".psd" or ".psb" or ".ai" or ".eps";

	public Task LoadAsync(string path) => loadingTask = LoadCoreAsync(path);
	private async Task LoadCoreAsync(string path)
	{
		var token = cancellation.Token;
		string extension = IO.Path.GetExtension(path).ToLowerInvariant();
		try
		{
			Caption.Text = Strings.CleanTagReviewLoading.GetLocalizedResource();
			if (IsArchive(extension))
			{
				var entries = await Task.Run(() =>
				{
					using var archive = new SevenZipExtractor(path);
					var result = new List<CleanTagArchiveEntry>();
					foreach (var entry in archive.GetArchiveFileData(path))
					{
						token.ThrowIfCancellationRequested();
						result.Add(new(entry.FileName, entry.IsDirectory, entry.Size, entry.Encrypted));
						if (result.Count > ArchiveLimit) break;
					}
					return result;
				}, token);
				if (disposed) return;
				ArchiveRows.ItemsSource = entries.Take(ArchiveLimit).ToArray();
				ArchiveRows.Visibility = Visibility.Visible;
				Caption.Text = (entries.Count > ArchiveLimit ? Strings.CleanTagPreviewArchiveLimited : Strings.CleanTagPreviewArchive).GetLocalizedResource();
				if (entries.Count == 0) ShowMessage(Strings.CleanTagPreviewArchiveEmpty.GetLocalizedResource());
				return;
			}
			if (extension == ".pdf" || (extension is ".ai" or ".eps" && await Task.Run(() => EmbeddedPreviewReader.IsPdf(path), token)))
			{
				var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
				var stream = await file.OpenReadAsync().AsTask(token);
				try
				{
					var document = await PdfDocument.LoadFromStreamAsync(stream).AsTask(token);
					if (disposed) return;
					pdf = document;
					pdfStream = stream;
					stream = null!;
				}
				finally { stream?.Dispose(); }
				await RenderPageAsync();
				return;
			}
			byte[]? embedded = await Task.Run(() => extension is ".psd" or ".psb"
				? EmbeddedPreviewReader.ReadPhotoshopThumbnail(path, token) : EmbeddedPreviewReader.ReadEpsThumbnail(path), token);
			if (embedded is not null)
			{
				var bitmap = await embedded.ToBitmapAsync();
				if (disposed) return;
				Picture.Source = bitmap;
				Caption.Text = Strings.CleanTagPreviewEmbedded.GetLocalizedResource();
				return;
			}
			await LoadFallbackAsync(path, token);
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			if (!disposed)
			{
				if (IsArchive(extension)) ShowMessage(Strings.CleanTagPreviewArchiveFailed.GetLocalizedResource());
				else await LoadFallbackAsync(path, token);
			}
		}
		finally { if (!disposed) Loading.IsActive = false; }
	}

	private async Task LoadFallbackAsync(string path, CancellationToken token)
	{
		try
		{
			var bytes = await FileThumbnailHelper.GetIconAsync(path, 256, false, IconOptions.ReturnThumbnailOnly);
			token.ThrowIfCancellationRequested();
			if (bytes is not null)
			{
				var bitmap = await bytes.ToBitmapAsync();
				if (disposed) return;
				Picture.Source = bitmap;
				Caption.Text = Strings.CleanTagPreviewEmbedded.GetLocalizedResource();
				return;
			}
			string extension = IO.Path.GetExtension(path);
			if (ShellPreviewViewModel.FindPreviewHandlerFor(extension, 0) is not null)
			{
				var listed = new ListedItem { ItemPath = path, ItemNameRaw = IO.Path.GetFileName(path), FileExtension = extension, PrimaryItemAttribute = StorageItemTypes.File };
				var model = new ShellPreviewViewModel(listed);
				shellModel = model;
				ShellHost.Content = new ShellPreview(model);
				Caption.Text = Strings.CleanTagPreviewSystem.GetLocalizedResource();
				return;
			}
		}
		catch (OperationCanceledException) { return; }
		catch (Exception) { }
		if (!disposed) ShowMessage(Strings.CleanTagPreviewUnavailable.GetLocalizedResource());
	}

	private async Task RenderPageAsync()
	{
		var token = cancellation.Token;
		await renderGate.WaitAsync(token);
		try
		{
			if (disposed || pdf is null) return;
			Loading.IsActive = true;
			Previous.IsEnabled = Next.IsEnabled = false;
			using var page = pdf.GetPage(pageIndex);
			using var stream = new InMemoryRandomAccessStream();
			await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 1500 }).AsTask(token);
			stream.Seek(0);
			var image = new BitmapImage();
			await image.SetSourceAsync(stream).AsTask(token);
			if (disposed) return;
			Picture.Source = image;
			Pages.Visibility = Visibility.Visible;
			PageLabel.Text = $"{pageIndex + 1} / {pdf.PageCount}";
			Caption.Text = Strings.CleanTagPreviewPdf.GetLocalizedResource();
			Previous.IsEnabled = pageIndex > 0;
			Next.IsEnabled = pageIndex + 1 < pdf.PageCount;
		}
		finally { renderGate.Release(); if (!disposed) Loading.IsActive = false; }
	}
	private async void Previous_Click(object sender, RoutedEventArgs e) { if (pageIndex > 0) { pageIndex--; await ChangePageAsync(); } }
	private async void Next_Click(object sender, RoutedEventArgs e) { if (pdf is not null && pageIndex + 1 < pdf.PageCount) { pageIndex++; await ChangePageAsync(); } }
	private async Task ChangePageAsync()
	{
		try { await RenderPageAsync(); }
		catch (OperationCanceledException) { }
		catch (Exception) { if (!disposed) ShowMessage(Strings.CleanTagPreviewUnavailable.GetLocalizedResource()); }
	}
	private void ShowMessage(string message) { Fallback.Text = message; Fallback.Visibility = Visibility.Visible; Caption.Text = ""; }
	private void Preview_Unloaded(object sender, RoutedEventArgs e) { if (ReleaseOnUnload) Dispose(); }
	public async Task ReleaseAsync() { Dispose(); await loadingTask; await ReleaseStreamAsync(); }
	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		cancellation.Cancel();
		shellModel?.PreviewControlBase_Unloaded(null, new());
		ShellHost.Content = null;
		Picture.Source = null;
		ArchiveRows.ItemsSource = null;
		_ = ReleaseStreamAsync();
	}
	private async Task ReleaseStreamAsync()
	{
		await renderGate.WaitAsync();
		try { pdf = null; pdfStream?.Dispose(); pdfStream = null; }
		finally { renderGate.Release(); }
	}
}

public sealed class CleanTagArchiveEntry
{
	public string Name { get; }
	public string Size { get; }
	public string Glyph { get; }
	public CleanTagArchiveEntry(string name, bool directory, ulong size, bool encrypted)
	{
		Name = name;
		Size = encrypted ? Strings.CleanTagPreviewEncrypted.GetLocalizedResource() : directory ? Strings.Folder.GetLocalizedResource() : FileEntry.FormatSize((long)Math.Min(size, long.MaxValue));
		Glyph = directory ? "\uE8B7" : "\uE8A5";
	}
}
