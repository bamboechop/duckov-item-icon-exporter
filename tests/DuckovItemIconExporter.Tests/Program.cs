using System.Text.Json;
using DuckovItemIconExporter;

var tests = new (string Name, Action Body)[]
{
    ("deterministic filename sanitization", FilenameSanitization),
    ("path traversal prevention", PathTraversal),
    ("TypeID uniqueness", TypeIdUniqueness),
    ("duplicate-entry handling", DuplicateEntries),
    ("stable ordering", StableOrdering),
    ("CSV escaping", CsvEscaping),
    ("HTML escaping", HtmlEscaping),
    ("JSON and CSV agreement", JsonCsvAgreement),
    ("manifest status accounting", StatusAccounting),
    ("output-directory isolation", OutputIsolation),
    ("failure rows remain visible", FailureRows),
    ("collision-free filenames", CollisionFreeNames),
    ("sprite region coordinates", SpriteCoordinates),
    ("rotation and flip transformations", RotationAndFlips)
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + exception.Message); }
}
return failed == 0 ? 0 : 1;

static void FilenameSanitization()
{
    Equal("Tote_Bag", ExportNaming.SanitizeComponent(" Tote Bag "));
    Equal("unnamed", ExportNaming.SanitizeComponent("../../"));
    Equal("A_B", ExportNaming.SanitizeComponent("A...B"));
}
static void PathTraversal()
{
    var root = Path.Combine(Path.GetTempPath(), "icon-exporter-tests", Guid.NewGuid().ToString("N"));
    Throws<ArgumentException>(() => ExportNaming.SafeChildPath(root, "../escape.png"));
    Throws<ArgumentException>(() => ExportNaming.SafeChildPath(root, "a/b.png"));
    True(ExportNaming.SafeChildPath(root, "safe.png").StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
}
static void TypeIdUniqueness()
{
    Throws<InvalidOperationException>(() => ManifestWriter.Ordered(new[] { Item(1), Item(1) }));
}
static void DuplicateEntries()
{
    var entries = new[] { Item(2), Item(1), Item(2) };
    Throws<InvalidOperationException>(() => ManifestWriter.Ordered(entries));
}
static void StableOrdering()
{
    var ordered = ManifestWriter.Ordered(new[] { Item(30), Item(-4), Item(2) });
    Equal(-4, ordered[0].TypeId); Equal(2, ordered[1].TypeId); Equal(30, ordered[2].TypeId);
}
static void CsvEscaping()
{
    var csv = ManifestWriter.BuildCsv(new[] { Item(1, "A,\"B\"") });
    True(csv.Contains("\"A,\"\"B\"\"\"", StringComparison.Ordinal));
}
static void HtmlEscaping()
{
    var html = ManifestWriter.BuildHtml(new[] { Item(1, "<script>&\"'") });
    True(html.Contains("<td>&lt;script&gt;&amp;&quot;&#39;</td>", StringComparison.Ordinal));
}
static void JsonCsvAgreement()
{
    var items = new[] { Item(4, "Alpha"), Item(9, "Beta") };
    var json = ManifestWriter.BuildJson(items);
    var csv = ManifestWriter.BuildCsv(items);
    using var document = JsonDocument.Parse(json);
    Equal(2, document.RootElement.GetArrayLength());
    Equal(3, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    Equal(4, document.RootElement[0].GetProperty("typeId").GetInt32());
    Equal(JsonValueKind.Array, document.RootElement[0].GetProperty("tags").ValueKind);
}
static void StatusAccounting()
{
    var items = new[] { Item(1, status: ExportStatus.Exported), Item(2, status: ExportStatus.NativeFallbackExported), Item(3, status: ExportStatus.NoIconAvailable), Item(4, status: ExportStatus.Failed) };
    var summary = ManifestWriter.BuildSummary(items);
    True(summary.Contains("Discovered: 4", StringComparison.Ordinal));
    True(summary.Contains("Successful: 2", StringComparison.Ordinal));
    True(summary.Contains("Unavailable: 1", StringComparison.Ordinal));
    True(summary.Contains("Failed: 1", StringComparison.Ordinal));
}
static void OutputIsolation()
{
    var root = Path.Combine(Path.GetTempPath(), "icon-exporter-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    ManifestWriter.WriteAll(root, new[] { Item(7) });
    True(File.Exists(Path.Combine(root, "items.json")));
    True(!Directory.GetFiles(Path.GetDirectoryName(root)!).Any(path => Path.GetFileName(path) == "items.json"));
    Directory.Delete(root, true);
}
static void FailureRows()
{
    var failure = Item(8, status: ExportStatus.Failed); failure.Reason = "GPU readback failed";
    var json = ManifestWriter.BuildJson(new[] { failure });
    var html = ManifestWriter.BuildHtml(new[] { failure });
    True(json.Contains("GPU readback failed", StringComparison.Ordinal));
    True(html.Contains("Failed", StringComparison.Ordinal));
}
static void CollisionFreeNames()
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var first = ExportNaming.CreateFileName(3, "same name", names);
    var second = ExportNaming.CreateFileName(3, "same name", names);
    Equal("3_same_name.png", first); Equal("3_same_name_2.png", second);
}
static void SpriteCoordinates()
{
    var rect = SpriteMath.NormalizeRegion(100, 80, 5, 7, 20, 10);
    var topLeft = SpriteMath.ToTopLeftRegion(80, rect);
    Equal(5, topLeft.X); Equal(63, topLeft.Y); Equal(20, topLeft.Width); Equal(10, topLeft.Height);
    Throws<ArgumentOutOfRangeException>(() => SpriteMath.NormalizeRegion(10, 10, 9, 0, 2, 1));
}
static void RotationAndFlips()
{
    var ninety = SpriteMath.TransformPoint(0, 0, 3, 2, SpriteRotation.Rotate90, false, false);
    Equal(1, ninety.X); Equal(0, ninety.Y);
    var flipped = SpriteMath.TransformPoint(0, 0, 3, 2, SpriteRotation.None, true, true);
    Equal(2, flipped.X); Equal(1, flipped.Y);
}
static ExportItem Item(int id, string name = "Item", ExportStatus status = ExportStatus.Exported) => new() { TypeId = id, InternalName = name, DisplayName = name, DisplayNameKey = name + "Key", Category = "Test", Status = status, OutputFileName = status == ExportStatus.Exported ? id + "_Item.png" : string.Empty };
static void Equal<T>(T expected, T actual) where T : notnull { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
