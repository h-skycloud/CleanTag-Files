using CleanTag.Core;

string root = Path.Combine(Path.GetTempPath(), "cleantag-files-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "nested"));
int checks = 0;
void Check(bool value, string message)
{
	if (!value) throw new InvalidOperationException(message);
	checks++;
}
try
{
	await File.WriteAllBytesAsync(Path.Combine(root, "one.txt"), new byte[1024]);
	await File.WriteAllBytesAsync(Path.Combine(root, "nested", "two.bin"), new byte[3072]);
	await File.WriteAllBytesAsync(Path.Combine(root, "zero"), []);
	var scanner = new StandardStorageScanner();
	var snapshot = await scanner.ScanAsync(root, new(), null, default);
	Check(snapshot.Status == StorageScanStatus.Completed, "Scan must complete");
	Check(snapshot.Nodes[0].Bytes == 4096 && snapshot.Nodes[0].Files == 3 && snapshot.Nodes[0].Folders == 1, "Hierarchy totals");
	Check(snapshot.Extensions.Sum(x => x.Bytes) == 4096, "Extension totals");
	var layout = TreemapLayoutEngine.Build(snapshot, 0, 800, 600);
	Check(layout.Tiles.Sum(x => x.RepresentedBytes) == 4096, "Treemap byte conservation");
	Check(Math.Abs(layout.Tiles.Sum(x => x.Bounds.Area) - 480000) < .1, "Treemap area conservation");
	Check(layout.Tiles.All(tile => layout.HitTest(tile.Bounds.X + tile.Bounds.Width / 2, tile.Bounds.Y + tile.Bounds.Height / 2)?.NodeId == tile.NodeId), "Hit testing");
	var filtered = TreemapLayoutEngine.Build(snapshot, 0, 800, 600, filter: n => n.Extension == ".txt");
	Check(filtered.Tiles.Sum(x => x.RepresentedBytes) == 1024 && snapshot.Nodes[0].Bytes == 4096, "Filter must not mutate snapshot");
	using var cancellation = new CancellationTokenSource();
	cancellation.Cancel();
	var cancelled = await scanner.ScanAsync(root, new(), null, cancellation.Token);
	Check(cancelled.Status == StorageScanStatus.Cancelled, "Cancellation produces partial result");
	var absent = await scanner.ScanAsync(Path.Combine(root, "missing"), new(), null, default);
	Check(absent.Status == StorageScanStatus.Failed && absent.IssueCount > 0, "Missing root is reported");
	Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 3, "Scan creates no catalog or sidecars");
	var first = new FileEntry(Path.Combine(root, "one.txt"), 1024, File.GetLastWriteTimeUtc(Path.Combine(root, "one.txt")).Ticks) { NativeId = "test-1" };
	var second = new FileEntry(Path.Combine(root, "zero"), 0, File.GetLastWriteTimeUtc(Path.Combine(root, "zero")).Ticks) { NativeId = "test-2" };
	var folder = new FileEntry(Path.Combine(root, "nested"), 0, Directory.GetLastWriteTimeUtc(Path.Combine(root, "nested")).Ticks) { IsDirectory = true, NativeId = "test-3" };
	var history = new List<ReviewRecord> { new(root, first, ReviewDecision.Keep), new(root, second, ReviewDecision.Later) };
	Check(new ReviewSession(root, [first, second], history, false).Remaining == 0, "Keep and Later are not repeated in pending queue");
	var later = new ReviewSession(root, [first, second], history, true);
	Check(later.Remaining == 1 && later.Current == second, "Deferred items have their own queue");
	later.Advance(); Check(later.Current is null && later.Remaining == 0, "Queue advances exactly once");
	Check(new ReviewSession(root, [first with { NativeId = "replacement" }], history, false).Remaining == 1, "Replacement is reviewed again");
	Check(new ReviewSession(root + "other", [first], history, false).Remaining == 1, "Decisions are scoped to chosen root");
	ReviewSafety.Validate(root, first, "test-1");
	ReviewSafety.Validate(root, folder, "test-3");
	Check(true, "File and folder revision validation");
	bool denied = false;
	try { ReviewSafety.Validate(root, first, "replaced"); } catch (IOException) { denied = true; }
	Check(denied, "Identity replacement blocks recycling");
	denied = false;
	try { ReviewSafety.Validate(root, first with { FullPath = Path.Combine(root, "nested", "two.bin") }, "test-1"); } catch (IOException) { denied = true; }
	Check(denied, "Out-of-scope descendant blocks recycling");
	var store = new ReviewDecisionStore(Path.Combine(root, "decisions.json"));
	await store.SaveAsync(history);
	var loaded = await store.LoadAsync();
	Check(loaded.Count == 2 && loaded[0].Entry.NativeId == "test-1" && loaded[0].Decision == ReviewDecision.Keep, "Decision persistence survives reload");
	await Task.WhenAll(store.RecordAsync(new(root, first, ReviewDecision.Later)),
		new ReviewDecisionStore(Path.Combine(root, "decisions.json")).RecordAsync(new(root, folder, ReviewDecision.Keep)));
	loaded = await store.LoadAsync();
	Check(loaded.Count == 3 && loaded.Single(record => record.Entry.FullPath == first.FullPath).Decision == ReviewDecision.Later,
		"Concurrent review windows merge records without losing decisions");
	var design = Path.Combine(root, "embedded.psd");
	byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
	using (var output = File.Create(design))
	{
		void Write32(uint value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, value); output.Write(bytes); }
		void Write16(ushort value) { Span<byte> bytes = stackalloc byte[2]; System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes, value); output.Write(bytes); }
		output.Write("8BPS"u8); Write16(1); output.Write(new byte[20]);
		Write32(0); Write32(44);
		output.Write("8BIM"u8); Write16(1036); output.WriteByte(0); output.WriteByte(0); Write32(32);
		Write32(1); output.Write(new byte[24]); output.Write(jpeg);
	}
	Check(EmbeddedPreviewReader.ReadPhotoshopThumbnail(design, default)?.SequenceEqual(jpeg) == true, "PSD embedded JPEG is recovered without reading layers");
	using (var output = new FileStream(design, FileMode.Open, FileAccess.Write)) { output.Position = 4; output.Write([0, 2]); }
	Check(EmbeddedPreviewReader.ReadPhotoshopThumbnail(design, default)?.SequenceEqual(jpeg) == true, "PSB embedded JPEG uses the same bounded resource parser");
	bool previewCancelled = false;
	try { EmbeddedPreviewReader.ReadPhotoshopThumbnail(design, cancellation.Token); } catch (OperationCanceledException) { previewCancelled = true; }
	Check(previewCancelled, "Embedded image loading honors cancellation");
	using (var output = new FileStream(design, FileMode.Open, FileAccess.Write)) { output.Position = 30; output.Write([0x7F, 0xFF, 0xFF, 0xFF]); }
	Check(EmbeddedPreviewReader.ReadPhotoshopThumbnail(design, default) is null, "Malformed resource length cannot allocate or read beyond the file");
	var illustrator = Path.Combine(root, "compatible.ai");
	await File.WriteAllTextAsync(illustrator, "%PDF-1.7\n% Illustrator PDF-compatible fixture");
	Check(EmbeddedPreviewReader.IsPdf(illustrator), "PDF-compatible AI is identified by content, not extension");
	await File.WriteAllTextAsync(illustrator, "%!PS-Adobe-3.0\n");
	Check(!EmbeddedPreviewReader.IsPdf(illustrator), "PostScript AI is not sent to the PDF renderer");
	Console.WriteLine($"PASS: {checks} analysis integration checks");
}
finally
{
	Directory.Delete(root, true);
}
