using System.IO.Enumeration;

namespace CleanTag.Core;

public sealed record FileEntry(string FullPath, long Size, long ModifiedTicks)
{
	public string? NativeId { get; init; }
	public bool IsDirectory { get; init; }
	public override string ToString() => Name;
	public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(FullPath)) is { Length: > 0 } name ? name : FullPath;
	public string Folder => Path.GetDirectoryName(FullPath) ?? "";
	public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
	public string Identity => $"{FullPath.ToUpperInvariant()}|{Size}|{ModifiedTicks}";
	public string SizeLabel => IsDirectory ? "İçerik boyutu hesaplanmadı" : FormatSize(Size);
	public string ModifiedLabel => new DateTime(ModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM yyyy · HH:mm");
	public string TypeLabel => IsDirectory ? "Klasör" : string.IsNullOrEmpty(Extension) ? "Dosya" : Extension.TrimStart('.').ToUpperInvariant();
	public string Category => IsDirectory ? "Klasörler" : Extension switch
	{
		".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".heic" or ".heif" or ".svg" or ".ico" or ".avif" => "Görseller",
		".psd" or ".psb" or ".ai" or ".indd" or ".skp" or ".skb" or ".dwg" or ".dxf" or ".rvt" or ".rfa" or ".max" or ".blend" or ".3ds" or ".obj" or ".fbx" or ".pln" => "Tasarım / model",
		".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".m4v" => "Videolar",
		".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" or ".wma" => "Sesler",
		".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" => "Arşivler",
		".pdf" or ".docx" or ".doc" or ".xlsx" or ".xls" or ".pptx" or ".ppt" or ".odt" or ".ods" or ".odp" or ".rtf" or ".txt" or ".md" or ".csv" => "Belgeler",
		_ => "Diğer"
	};
	public static string FormatSize(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB", "TB"];
		double value = bytes; int i = 0;
		while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
		return $"{value:0.##} {units[i]}";
	}
}
