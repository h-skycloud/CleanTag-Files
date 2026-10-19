// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using CleanTag.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IO = System.IO;
namespace Files.App.UserControls.CleanTag;
internal static class CleanTagTagEditor
{
	public static async Task<bool> EditAsync(string path, bool isDirectory, XamlRoot xamlRoot)
	{
		var tags = Ioc.Default.GetRequiredService<IFileTagsService>();
		var tagSettings = Ioc.Default.GetRequiredService<IFileTagsSettingsService>();
		var storage = Ioc.Default.GetRequiredService<IStorageService>();
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
				XamlRoot = xamlRoot,
				Title = Strings.CleanTagTags.GetLocalizedResource(),
				Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
				PrimaryButtonText = Strings.CleanTagSave.GetLocalizedResource(),
				CloseButtonText = Strings.Cancel.GetLocalizedResource()
			};
			if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
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
				return true;
			}
			else throw new IO.IOException(Strings.CleanTagError.GetLocalizedResource());
		}
		catch { throw; }
	}
}
