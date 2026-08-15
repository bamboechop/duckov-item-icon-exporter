using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DuckovItemIconExporter
{
    public enum ExportStatus
    {
        Exported,
        NativeFallbackExported,
        NoIconAvailable,
        Failed
    }

    public sealed class ExportItem
    {
        public int TypeId { get; set; }
        public string InternalName { get; set; } = string.Empty;
        public string DisplayNameKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Quality { get; set; }
        public string Category { get; set; } = string.Empty;
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public string Caliber { get; set; } = string.Empty;
        public string SpriteName { get; set; } = string.Empty;
        public string TextureName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public string OutputFileName { get; set; } = string.Empty;
        public ExportStatus Status { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public static class ExportNaming
    {
        public static string CreateFileName(int typeId, string internalName, ISet<string> reservedNames)
        {
            if (reservedNames == null) throw new ArgumentNullException(nameof(reservedNames));
            var stem = typeId.ToString(CultureInfo.InvariantCulture) + "_" + SanitizeComponent(internalName);
            var candidate = stem + ".png";
            var suffix = 2;
            while (!reservedNames.Add(candidate))
            {
                candidate = stem + "_" + suffix.ToString(CultureInfo.InvariantCulture) + ".png";
                suffix++;
            }
            return candidate;
        }

        public static string SanitizeComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unnamed";
            var builder = new StringBuilder(value.Length);
            foreach (var character in value.Trim())
            {
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') || character == '-' || character == '_')
                {
                    builder.Append(character);
                }
                else if (char.IsWhiteSpace(character) || character == '.')
                {
                    builder.Append('_');
                }
            }
            var result = CollapseUnderscores(builder.ToString()).Trim('_', '.');
            if (result.Length == 0) result = "unnamed";
            return result.Length <= 80 ? result : result.Substring(0, 80);
        }

        private static string CollapseUnderscores(string value)
        {
            var builder = new StringBuilder(value.Length);
            var previousUnderscore = false;
            foreach (var character in value)
            {
                if (character == '_')
                {
                    if (!previousUnderscore) builder.Append(character);
                    previousUnderscore = true;
                }
                else { builder.Append(character); previousUnderscore = false; }
            }
            return builder.ToString();
        }

        public static string SafeChildPath(string root, string fileName)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A root path is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
                throw new ArgumentException("The output filename must be a single path-safe filename.", nameof(fileName));
            var fullRoot = Path.GetFullPath(root);
            var fullChild = Path.GetFullPath(Path.Combine(fullRoot, fileName));
            var rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullChild.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output path escaped its export directory.");
            return fullChild;
        }
    }

    public static class SpriteMath
    {
        public static PixelRect NormalizeRegion(int textureWidth, int textureHeight, int x, int y, int width, int height)
        {
            if (textureWidth <= 0 || textureHeight <= 0 || width <= 0 || height <= 0 || x < 0 || y < 0 ||
                x + width > textureWidth || y + height > textureHeight)
                throw new ArgumentOutOfRangeException(nameof(width), "Sprite region is outside its texture.");
            return new PixelRect(x, y, width, height);
        }

        public static PixelRect ToTopLeftRegion(int textureHeight, PixelRect unityBottomLeftRegion)
        {
            return new PixelRect(unityBottomLeftRegion.X, textureHeight - unityBottomLeftRegion.Y - unityBottomLeftRegion.Height,
                unityBottomLeftRegion.Width, unityBottomLeftRegion.Height);
        }

        public static PixelPoint TransformPoint(int x, int y, int width, int height, SpriteRotation rotation, bool flipX, bool flipY)
        {
            var point = rotation == SpriteRotation.None ? new PixelPoint(x, y) :
                rotation == SpriteRotation.Rotate90 ? new PixelPoint(height - 1 - y, x) :
                rotation == SpriteRotation.Rotate180 ? new PixelPoint(width - 1 - x, height - 1 - y) :
                new PixelPoint(y, width - 1 - x);
            var rotatedWidth = rotation == SpriteRotation.Rotate90 || rotation == SpriteRotation.Rotate270 ? height : width;
            var rotatedHeight = rotation == SpriteRotation.Rotate90 || rotation == SpriteRotation.Rotate270 ? width : height;
            return new PixelPoint(flipX ? rotatedWidth - 1 - point.X : point.X, flipY ? rotatedHeight - 1 - point.Y : point.Y);
        }
    }

    public enum SpriteRotation { None, Rotate90, Rotate180, Rotate270 }
    public readonly struct PixelRect { public PixelRect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; } public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; } }
    public readonly struct PixelPoint { public PixelPoint(int x, int y) { X = x; Y = y; } public int X { get; } public int Y { get; } }

    public static class ManifestWriter
    {
        public static IReadOnlyList<ExportItem> Ordered(IEnumerable<ExportItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var ordered = items.OrderBy(item => item.TypeId).ToList();
            if (ordered.GroupBy(item => item.TypeId).Any(group => group.Count() != 1)) throw new InvalidOperationException("Every TypeID must have exactly one manifest row.");
            return ordered;
        }

        public static void WriteAll(string exportDirectory, IEnumerable<ExportItem> sourceItems)
        {
            var items = Ordered(sourceItems);
            Directory.CreateDirectory(exportDirectory);
            File.WriteAllText(Path.Combine(exportDirectory, "items.json"), BuildJson(items), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDirectory, "items.csv"), BuildCsv(items), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDirectory, "index.html"), BuildHtml(items), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(exportDirectory, "summary.txt"), BuildSummary(items), new UTF8Encoding(false));
        }

        public static string BuildJson(IReadOnlyList<ExportItem> items)
        {
            var builder = new StringBuilder("[\n");
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                builder.Append("  {");
                AppendJson(builder, "typeId", item.TypeId.ToString(CultureInfo.InvariantCulture), false); AppendJson(builder, "internalName", item.InternalName, true);
                AppendJson(builder, "displayNameKey", item.DisplayNameKey, true); AppendJson(builder, "displayName", item.DisplayName, true);
                AppendJson(builder, "quality", item.Quality.ToString(CultureInfo.InvariantCulture), false); AppendJson(builder, "category", item.Category, true);
                AppendJson(builder, "tags", string.Join("|", item.Tags ?? Array.Empty<string>()), true); AppendJson(builder, "caliber", item.Caliber, true);
                AppendJson(builder, "spriteName", item.SpriteName, true); AppendJson(builder, "textureName", item.TextureName, true);
                AppendJson(builder, "width", item.Width.ToString(CultureInfo.InvariantCulture), false); AppendJson(builder, "height", item.Height.ToString(CultureInfo.InvariantCulture), false);
                AppendJson(builder, "outputPng", item.OutputFileName, true); AppendJson(builder, "status", item.Status.ToString(), true); AppendJson(builder, "reason", item.Reason, true, false);
                builder.Append('}'); if (index + 1 < items.Count) builder.Append(','); builder.Append('\n');
            }
            return builder.Append("]\n").ToString();
        }

        public static string BuildCsv(IReadOnlyList<ExportItem> items)
        {
            var builder = new StringBuilder("TypeID,InternalName,DisplayNameKey,DisplayName,Quality,Category,Tags,Caliber,SpriteName,TextureName,Width,Height,OutputPNG,Status,Reason\n");
            foreach (var item in items)
                builder.Append(string.Join(",", new[] { item.TypeId.ToString(CultureInfo.InvariantCulture), item.InternalName, item.DisplayNameKey, item.DisplayName, item.Quality.ToString(CultureInfo.InvariantCulture), item.Category, string.Join("|", item.Tags ?? Array.Empty<string>()), item.Caliber, item.SpriteName, item.TextureName, item.Width.ToString(CultureInfo.InvariantCulture), item.Height.ToString(CultureInfo.InvariantCulture), item.OutputFileName, item.Status.ToString(), item.Reason }.Select(Csv))).Append('\n');
            return builder.ToString();
        }

        public static string BuildHtml(IReadOnlyList<ExportItem> items)
        {
            var builder = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>Duckov item icons</title><style>body{font:14px system-ui;margin:20px}input{width:100%;padding:8px}table{border-collapse:collapse;width:100%;margin-top:14px}td,th{border:1px solid #ccc;padding:6px;text-align:left}img{max-width:64px;max-height:64px}</style></head><body><h1>Duckov item icons</h1><input id=\"q\" placeholder=\"Search TypeID, name, or status\"><table><thead><tr><th>Icon</th><th>TypeID</th><th>Display name</th><th>Internal name</th><th>Dimensions</th><th>Status</th></tr></thead><tbody>");
            foreach (var item in items)
            {
                var searchable = item.TypeId + " " + item.DisplayName + " " + item.InternalName + " " + item.Status;
                builder.Append("<tr data-search=\"").Append(Html(searchable)).Append("\"><td>");
                if (!string.IsNullOrEmpty(item.OutputFileName)) builder.Append("<img src=\"icons/").Append(Html(item.OutputFileName)).Append("\" alt=\"\">");
                builder.Append("</td><td>").Append(item.TypeId).Append("</td><td>").Append(Html(item.DisplayName)).Append("</td><td>").Append(Html(item.InternalName)).Append("</td><td>").Append(item.Width).Append('×').Append(item.Height).Append("</td><td title=\"").Append(Html(item.Reason)).Append("\">").Append(Html(item.Status.ToString())).Append("</td></tr>");
            }
            return builder.Append("</tbody></table><script>q.oninput=()=>document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.dataset.search.toLowerCase().includes(q.value.toLowerCase()))</script></body></html>\n").ToString();
        }

        public static string BuildSummary(IReadOnlyList<ExportItem> items)
        {
            var counts = items.GroupBy(item => item.Status).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
            var successful = Count(counts, ExportStatus.Exported) + Count(counts, ExportStatus.NativeFallbackExported);
            return "Duckov Item Icon Exporter summary\nDiscovered: " + items.Count + "\nSuccessful: " + successful + "\nUnavailable: " + Count(counts, ExportStatus.NoIconAvailable) + "\nFailed: " + Count(counts, ExportStatus.Failed) + "\n";
        }

        private static int Count(IReadOnlyDictionary<ExportStatus, int> counts, ExportStatus status) { return counts.TryGetValue(status, out var value) ? value : 0; }
        private static void AppendJson(StringBuilder builder, string name, string value, bool quoted, bool comma = true) { builder.Append('\"').Append(name).Append("\":"); if (quoted) builder.Append('\"').Append(Json(value)).Append('\"'); else builder.Append(value); if (comma) builder.Append(','); }
        private static string Csv(string? value) { return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\""; }
        private static string Json(string? value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"); }
        private static string Html(string? value) { return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;"); }
    }
}
