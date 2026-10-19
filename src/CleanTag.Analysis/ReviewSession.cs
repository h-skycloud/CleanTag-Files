// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanTag.Core;

public enum ReviewDecision { Keep, Later, Recycled }
public sealed record ReviewRecord(string Root, FileEntry Entry, ReviewDecision Decision);

public sealed class ReviewSession
{
	private readonly Queue<FileEntry> pending;
	public FileEntry? Current => pending.TryPeek(out var item) ? item : null;
	public int Remaining => pending.Count;
	public ReviewSession(string root, IEnumerable<FileEntry> entries, IEnumerable<ReviewRecord> history, bool laterOnly)
	{
		var decisions = history.Where(r => string.Equals(r.Root, root, StringComparison.OrdinalIgnoreCase))
			.ToDictionary(r => Key(r.Entry), r => r.Decision, StringComparer.OrdinalIgnoreCase);
		pending = new(entries.Where(e => laterOnly
			? decisions.TryGetValue(Key(e), out var decision) && decision == ReviewDecision.Later
			: !decisions.ContainsKey(Key(e))));
	}
	public void Advance() => pending.TryDequeue(out _);
	public static string Key(FileEntry entry) => $"{entry.Identity}|{entry.IsDirectory}|{entry.NativeId}";
}

public sealed class ReviewDecisionStore(string path)
{
	public async Task<List<ReviewRecord>> RecordAsync(ReviewRecord record, CancellationToken token = default)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		FileStream? lease = null;
		for (int attempt = 0; lease is null; attempt++)
		{
			token.ThrowIfCancellationRequested();
			try { lease = new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
			catch (IOException) when (attempt < 80) { await Task.Delay(25, token); }
		}
		await using (lease)
		{
			var records = await LoadAsync(token);
			records.RemoveAll(old => string.Equals(old.Root, record.Root, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(old.Entry.FullPath, record.Entry.FullPath, StringComparison.OrdinalIgnoreCase) && old.Entry.IsDirectory == record.Entry.IsDirectory);
			records.Add(record);
			await SaveAsync(records, token);
			return records;
		}
	}
	public async Task<List<ReviewRecord>> LoadAsync(CancellationToken token = default)
	{
		if (!File.Exists(path)) return [];
		await using var stream = File.OpenRead(path);
		return await JsonSerializer.DeserializeAsync(stream, ReviewJsonContext.Default.ListReviewRecord, token) ?? [];
	}
	public async Task SaveAsync(List<ReviewRecord> records, CancellationToken token = default)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string temporary = path + ".tmp";
		await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			await JsonSerializer.SerializeAsync(stream, records, ReviewJsonContext.Default.ListReviewRecord, token);
			await stream.FlushAsync(token);
		}
		File.Move(temporary, path, true);
	}
}

public static class ReviewSafety
{
	public static void Validate(string root, FileEntry entry, string? nativeId)
	{
		string parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(entry.FullPath)!);
		if (!string.Equals(parent, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
			throw new IOException("The item is outside the selected folder.");
		FileSafety.CheckAncestors(parent);
		FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(entry.FullPath) : new FileInfo(entry.FullPath);
		info.Refresh();
		if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline)) != 0 ||
			((info.Attributes & FileAttributes.Directory) != 0) != entry.IsDirectory ||
			info.LastWriteTimeUtc.Ticks != entry.ModifiedTicks ||
			(!entry.IsDirectory && ((FileInfo)info).Length != entry.Size) ||
			entry.NativeId is null || nativeId != entry.NativeId)
			throw new IOException("The item changed or cannot be safely recycled. Refresh the folder.");
	}
}

[JsonSerializable(typeof(List<ReviewRecord>))]
internal partial class ReviewJsonContext : JsonSerializerContext;
