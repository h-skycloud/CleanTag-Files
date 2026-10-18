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
	Console.WriteLine($"PASS: {checks} analysis integration checks");
}
finally
{
	Directory.Delete(root, true);
}
