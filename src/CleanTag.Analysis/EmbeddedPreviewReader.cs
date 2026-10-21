// Copyright (c) CleanTag contributors. Licensed under the MIT License.
using System.Buffers.Binary;

namespace CleanTag.Core;

public static class EmbeddedPreviewReader
{
	private const int ImageLimit = 8_000_000;
	public static bool IsPdf(string path)
	{
		using var stream = Open(path);
		Span<byte> header = stackalloc byte[1024];
		int length = stream.Read(header);
		return header[..length].IndexOf("%PDF-"u8) >= 0;
	}

	public static byte[]? ReadPhotoshopThumbnail(string path, CancellationToken token)
	{
		using var stream = Open(path);
		Span<byte> header = stackalloc byte[26];
		stream.ReadExactly(header);
		if (!header[..4].SequenceEqual("8BPS"u8) || BinaryPrimitives.ReadUInt16BigEndian(header[4..]) is not (1 or 2)) return null;
		uint Read32() { Span<byte> bytes = stackalloc byte[4]; stream.ReadExactly(bytes); return BinaryPrimitives.ReadUInt32BigEndian(bytes); }
		ushort Read16() { Span<byte> bytes = stackalloc byte[2]; stream.ReadExactly(bytes); return BinaryPrimitives.ReadUInt16BigEndian(bytes); }
		uint colorLength = Read32();
		if (colorLength > stream.Length - stream.Position - 4) return null;
		stream.Seek(colorLength, SeekOrigin.Current);
		uint resourceLength = Read32();
		if (resourceLength > stream.Length - stream.Position) return null;
		long end = stream.Position + resourceLength;
		Span<byte> signature = stackalloc byte[4];
		for (int count = 0; stream.Position + 12 <= end && count < 10000; count++)
		{
			token.ThrowIfCancellationRequested();
			stream.ReadExactly(signature);
			if (!signature.SequenceEqual("8BIM"u8)) return null;
			ushort id = Read16();
			int nameLength = stream.ReadByte();
			if (nameLength < 0 || stream.Position + nameLength + ((nameLength + 1) % 2) + 4 > end) return null;
			stream.Seek(nameLength + ((nameLength + 1) % 2), SeekOrigin.Current);
			uint size = Read32();
			long next = stream.Position + size + (size % 2);
			if (next > end) return null;
			if (id is 1033 or 1036 && size > 28 && size <= ImageLimit && Read32() == 1)
			{
				stream.Seek(24, SeekOrigin.Current);
				var jpeg = new byte[size - 28];
				stream.ReadExactly(jpeg);
				return jpeg;
			}
			stream.Position = next;
		}
		return null;
	}

	public static byte[]? ReadEpsThumbnail(string path)
	{
		using var stream = Open(path);
		Span<byte> header = stackalloc byte[30];
		if (stream.Read(header) < header.Length || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0xC6D3D0C5) return null;
		uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
		uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
		if (offset < 30 || size == 0 || size > ImageLimit || offset > stream.Length || size > stream.Length - offset) return null;
		stream.Position = offset;
		var bytes = new byte[size];
		stream.ReadExactly(bytes);
		return bytes;
	}

	private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
