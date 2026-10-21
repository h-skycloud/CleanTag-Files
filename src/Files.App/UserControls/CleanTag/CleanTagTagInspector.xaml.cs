// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using IO = System.IO;

namespace Files.App.UserControls.CleanTag;

public sealed partial class CleanTagTagInspector : UserControl, IDisposable
{
	private readonly IFileTagsSettingsService settings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
	private string? path;
	private bool directory, busy, disposed;
	public event Action<bool>? BusyChanged;
	public event Action<Exception>? Failed;
	public CleanTagTagInspector()
	{
		InitializeComponent();
		settings.OnTagsUpdated += TagsUpdated;
		SetTarget(null, false);
	}
	public void SetTarget(string? target, bool isDirectory)
	{
		path = target;
		directory = isDirectory;
		TargetName.Text = target is null ? Strings.CleanTagInspectorEmpty.GetLocalizedResource() : IO.Path.GetFileName(target);
		IsEnabled = target is not null && !busy;
		Refresh();
	}
	private void TagsUpdated(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !busy) Refresh(); });
	private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (Choices is not null) Refresh(); }
	private void Refresh()
	{
		Choices.Children.Clear();
		if (path is null || disposed) return;
		try
		{
			var assigned = FileTagsHelper.ReadFileTag(path);
			foreach (var tag in settings.FileTagList.Where(tag => tag.Name.Contains(Search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
				.OrderByDescending(tag => assigned.Contains(tag.Uid)).ThenBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
				label.Children.Add(new FontIcon { Glyph = "\uE8EC", FontSize = 14, Foreground = new SolidColorBrush(ColorHelpers.FromHex(tag.Color)) });
				label.Children.Add(new TextBlock { Text = tag.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
				var box = new CheckBox { Content = label, IsChecked = assigned.Contains(tag.Uid), HorizontalAlignment = HorizontalAlignment.Stretch };
				Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, tag.Name);
				box.Click += async (_, _) => await ChangeAsync(tag.Uid, box.IsChecked == true);
				Choices.Children.Add(box);
			}
			if (Choices.Children.Count == 0) Choices.Children.Add(new TextBlock { Text = Strings.CleanTagInspectorNoTags.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap });
		}
		catch (Exception ex) { Failed?.Invoke(ex); }
	}
	private async Task ChangeAsync(string uid, bool selected)
	{
		if (busy || disposed || path is null) return;
		await WriteAsync(path, directory, uid, selected);
	}
	private async Task WriteAsync(string target, bool folder, string uid, bool selected)
	{
		busy = true;
		IsEnabled = false;
		BusyChanged?.Invoke(true);
		try
		{
			FileSafety.CheckAncestors(folder ? target : IO.Path.GetDirectoryName(target)!);
			var identity = FileTagsHelper.GetFileFRN(target);
			var modified = IO.File.GetLastWriteTimeUtc(target);
			var storage = Ioc.Default.GetRequiredService<IStorageService>();
			var storable = await storage.TryGetStorableAsync(target) ?? throw new IO.FileNotFoundException();
			FileSafety.CheckAncestors(folder ? target : IO.Path.GetDirectoryName(target)!);
			if ((IO.File.GetAttributes(target) & (IO.FileAttributes.ReparsePoint | IO.FileAttributes.Offline | IO.FileAttributes.System)) != 0 ||
				FileTagsHelper.GetFileFRN(target) != identity || IO.File.GetLastWriteTimeUtc(target) != modified)
				throw new IO.IOException(Strings.CleanTagChanged.GetLocalizedResource());
			var current = FileTagsHelper.ReadFileTag(target);
			var updated = selected ? current.Append(uid).Distinct().ToArray() : current.Where(value => value != uid).ToArray();
			if (!await Ioc.Default.GetRequiredService<IFileTagsService>().SetFileTagAsync(storable, updated))
				throw new IO.IOException(Strings.CleanTagError.GetLocalizedResource());
			FileTagsHelper.GetDbInstance().SetTags(target, identity, updated);
		}
		catch (Exception ex) { if (!disposed) Failed?.Invoke(ex); }
		finally
		{
			busy = false;
			if (!disposed) { IsEnabled = path is not null; Refresh(); BusyChanged?.Invoke(false); }
		}
	}
	private async void Create_Click(object sender, RoutedEventArgs e) => await CreateAsync();
	private async void NewName_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == VirtualKey.Enter) { e.Handled = true; await CreateAsync(); }
	}
	private async Task CreateAsync()
	{
		if (busy || disposed || path is null || string.IsNullOrWhiteSpace(NewName.Text)) return;
		string name = NewName.Text.Trim();
		if (name.Length > 100) return;
		try
		{
			if (!settings.GetTagsByName(name).Any()) settings.CreateNewTag(name, "#0078D4");
			var tag = settings.GetTagsByName(name).First();
			NewName.Text = "";
			await WriteAsync(path, directory, tag.Uid, true);
		}
		catch (Exception ex) { Failed?.Invoke(ex); }
	}
	public void Dispose() { disposed = true; path = null; settings.OnTagsUpdated -= TagsUpdated; }
}
