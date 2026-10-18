// Copyright (c) CleanTag contributors. Licensed under the MIT License.
namespace CleanTag.Core;
public static class FileSafety
{
	public static void CheckAncestors(string path)
	{
		for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
			if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
				throw new IOException("Reparse points are not followed.");
	}
}
