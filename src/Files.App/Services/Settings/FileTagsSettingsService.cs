// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Serialization.Implementation;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using Windows.Storage;
using CleanTag.Core;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Services.Settings
{
	[JsonSourceGenerationOptions(WriteIndented = true)]
	[JsonSerializable(typeof(object))]
	[JsonSerializable(typeof(ConcurrentDictionary<string, JsonElement>))]
	[JsonSerializable(typeof(List<TagViewModel>))]
	[JsonSerializable(typeof(IList<TagViewModel>))]
	[JsonSerializable(typeof(Dictionary<string, IList<TagViewModel>>))]
	internal sealed partial class FileTagsSettingsJsonSerializationContext : JsonSerializerContext
	{
	}

	internal sealed class FileTagsSettingsService : BaseJsonSettings, IFileTagsSettingsService
	{
		public event EventHandler? OnSettingImportedEvent;

		public event EventHandler? OnTagsUpdated;

		private static readonly List<TagViewModel> DefaultFileTags =
		[
			new("Home", "#0072BD", "f7e0e137-2eb5-4fa4-a50d-ddd65df17c34"),
			new("Work", "#D95319", "c84a8131-c4de-47d9-9440-26e859d14b3d"),
			new("Photos", "#EDB120", "d4b8d4bd-ceaf-4e58-ac61-a185fcf96c5d"),
			new("Important", "#77AC30", "79376daf-c44a-4fe4-aa3b-8b30baea453e")
		];

		public FileTagsSettingsService()
		{
			var settingsSerializer = new DefaultSettingsSerializer();
			SettingsSerializer = settingsSerializer;

			Initialize(Path.Combine(ApplicationData.Current.LocalFolder.Path,
				Constants.LocalSettings.SettingsFolderName, Constants.LocalSettings.FileTagSettingsFileName));

			var jsonSettingsSerializer = new DefaultJsonSettingsSerializer();
			JsonSettingsSerializer = jsonSettingsSerializer;
			JsonSettingsDatabase = new CachingJsonSettingsDatabase(
				settingsSerializer,
				jsonSettingsSerializer,
				FileTagsSettingsJsonSerializationContext.Default);
		}

		public IList<TagViewModel> FileTagList
		{
			get
			{
				var tags = Get<List<TagViewModel>>(DefaultFileTags) ?? DefaultFileTags;

				foreach (var tag in tags)
					tag.Color = ColorHelpers.FromHex(tag.Color).ToString();

				return tags;
			}
			set
			{
				Set(value);
				OnTagsUpdated?.Invoke(this, EventArgs.Empty);
			}
		}

		public TagViewModel? GetTagById(string uid)
		{
			if (FileTagList.Any(x => x.Uid is null))
			{
				App.Logger.LogWarning("Tags file is invalid, regenerate");
				FileTagList = DefaultFileTags;
			}

			var tag = FileTagList.SingleOrDefault(x => x.Uid == uid);

			if (!string.IsNullOrEmpty(uid) && tag is null)
			{
				tag = new TagViewModel(Strings.Unknown.GetLocalizedResource(), "#9ea3a1", uid);
				FileTagList = FileTagList.Append(tag).ToList();
			}

			return tag;
		}

		public IList<TagViewModel>? GetTagsByIds(string[]? uids)
		{
			return uids is null || uids.Length == 0
				? null
				: uids.Select(GetTagById).WhereNotNull().ToList();
		}

		public IEnumerable<TagViewModel> GetTagsByName(string tagName)
		{
			return FileTagList.Where(x => x.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
		}

		public void CreateNewTag(string newTagName, string color)
		{
			var newTag = new TagViewModel(
				newTagName,
				color,
				Guid.NewGuid().ToString());

			var oldTags = FileTagList.ToList();
			oldTags.Add(newTag);
			FileTagList = oldTags;
		}

		public void EditTag(string uid, string name, string color)
		{
			var index = GetTagIndex(uid);
			if (index == -1)
				return;

			var oldTags = FileTagList.ToList();
			oldTags.RemoveAt(index);
			oldTags.Insert(index, new TagViewModel(name, color, uid));
			FileTagList = oldTags;
		}

		private readonly SemaphoreSlim deleteGate = new(1, 1);

		public async Task<bool> DeleteTagAsync(string uid)
		{
			await deleteGate.WaitAsync();
			try
			{
				if (GetTagIndex(uid) == -1)
					return true;
				if (!await UntagAllFilesAsync(uid))
					return false;
				FileTagList = FileTagList.Where(tag => tag.Uid != uid).ToList();
				return true;
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Could not complete tag deletion; its definition was retained.");
				return false;
			}
			finally { deleteGate.Release(); }
		}

		public override bool ImportSettings(object import)
		{
			if (import is string importString)
			{
				var settings = GetJsonSettingsSerializer().DeserializeFromJson(importString, FileTagsSettingsJsonSerializationContext.Default.DictionaryStringIListTagViewModel);
				if (settings is not null && settings.TryGetValue("FileTagList", out var importedTags))
					FileTagList = importedTags;
			}
			else if (import is IList<TagViewModel> importList)
			{
				FileTagList = importList;
			}

			FileTagList ??= DefaultFileTags;

			if (FileTagList is not null)
			{
				FlushSettings();
				OnSettingImportedEvent?.Invoke(this, EventArgs.Empty);
				return true;
			}

			return false;
		}

		public override object ExportSettings()
		{
			var settings = new Dictionary<string, IList<TagViewModel>>
			{
				{ "FileTagList", FileTagList }
			};

			// Serialize settings to JSON format
			return GetJsonSettingsSerializer().SerializeToJson(settings, FileTagsSettingsJsonSerializationContext.Default.DictionaryStringIListTagViewModel);
		}

		private int GetTagIndex(string uid)
		{
			for (int i = 0; i < FileTagList.Count; i++)
			{
				if (FileTagList[i].Uid == uid)
					return i;
			}

			return -1;
		}

		private async Task<bool> UntagAllFilesAsync(string uid)
		{
			var database = FileTagsHelper.GetDbInstance();
			bool succeeded = true;
			foreach (var item in database.GetAll().Where(item => item.Tags.Contains(uid)))
			{
				try
				{
					// Resolve renamed items by identity before changing their current ADS.
					var path = item.Frn is not null
						? Win32Helper.PathFromFileId(item.Frn.Value, item.FilePath)?.Replace(@"\\?\", "", StringComparison.Ordinal)
						: item.FilePath;
					if (path is null)
						throw new IOException("The tagged item could not be resolved.");
					var attributes = File.GetAttributes(path);
					FileSafety.CheckAncestors(attributes.HasFlag(FileAttributes.Directory) ? path : Path.GetDirectoryName(path)!);
					if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System)) != 0)
						throw new IOException("The tagged item is not available for writing.");
					var identity = FileTagsHelper.GetFileFRN(path);
					if (item.Frn is not null && identity != item.Frn)
						throw new IOException("The tagged item has changed.");
					var remaining = FileTagsHelper.ReadFileTag(path).Where(tag => tag != uid).ToArray();
					if (!await FileTagsHelper.WriteFileTagAsync(path, remaining))
						throw new IOException("The tag could not be removed.");
					if (!path.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
						database.UpdateTag(item.FilePath, identity, path);
					database.SetTags(path, identity, remaining);
				}
				catch (Exception ex)
				{
					succeeded = false;
					App.Logger.LogWarning(ex, "Could not remove a tag from an indexed item.");
				}
				await Task.Yield();
			}
			return succeeded;
		}

		private IJsonSettingsSerializer GetJsonSettingsSerializer()
		{
			return JsonSettingsSerializer
				?? throw new InvalidOperationException("The JSON settings serializer has not been initialized.");
		}
	}
}
