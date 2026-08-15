using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duckov.Modding;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovItemIconExporter
{
    /// <summary>Duckov's native mod-loader entry point. It performs exactly one base-game export per activation.</summary>
    public sealed class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string Prefix = "[DuckovItemIconExporter]";
        private const int IconsPerFrame = 4;
        private Coroutine? exportRoutine;
        private bool exportStarted;
        private SpriteRenderSurface? renderSurface;

        protected override void OnAfterSetup()
        {
            Debug.Log(Prefix + " enabled; waiting for the native base-game item collection.");
            exportRoutine = StartCoroutine(ExportWhenReady());
        }

        protected override void OnBeforeDeactivate()
        {
            if (exportRoutine != null) StopCoroutine(exportRoutine);
            exportRoutine = null;
            renderSurface?.Dispose();
            renderSurface = null;
            Debug.Log(Prefix + " shut down cleanly.");
        }

        private IEnumerator ExportWhenReady()
        {
            for (var frame = 0; frame < 900; frame++)
            {
                var collection = ItemAssetsCollection.Instance;
                if (collection != null && collection.entries != null)
                {
                    yield return Export(collection);
                    yield break;
                }
                yield return null;
            }
            Debug.LogError(Prefix + " native ItemAssetsCollection.Instance did not become available within 900 frames; no export was created.");
        }

        private IEnumerator Export(ItemAssetsCollection collection)
        {
            if (exportStarted)
            {
                yield break;
            }
            exportStarted = true;

            var directory = CreateExportDirectory();
            var iconDirectory = Path.Combine(directory, "icons");
            Directory.CreateDirectory(iconDirectory);
            Debug.Log(Prefix + " beginning one-time export to " + directory);

            var allEntries = collection.entries.Where(entry => entry != null).ToList();
            var duplicateIds = new HashSet<int>(allEntries.GroupBy(entry => entry.typeID).Where(group => group.Count() > 1).Select(group => group.Key));
            var entries = allEntries.GroupBy(entry => entry.typeID).Select(group => group.First()).OrderBy(entry => entry.typeID).ToList();
            if (entries.Count == 0)
            {
                Debug.LogError(Prefix + " native ItemAssetsCollection is available but has no base-game entries; no misleading empty export was created.");
                TryDeleteEmptyDirectory(directory);
                yield break;
            }

            var results = new List<ExportItem>(entries.Count);
            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            renderSurface = new SpriteRenderSurface();
            for (var index = 0; index < entries.Count; index++)
            {
                results.Add(ExportEntry(entries[index], duplicateIds.Contains(entries[index].typeID), iconDirectory, fileNames));
                if ((index + 1) % IconsPerFrame == 0) yield return null;
            }

            try
            {
                ManifestWriter.WriteAll(directory, results);
                var ordered = ManifestWriter.Ordered(results);
                var unavailable = ordered.Count(item => item.Status == ExportStatus.NoIconAvailable);
                var failed = ordered.Count(item => item.Status == ExportStatus.Failed);
                var successful = ordered.Count - unavailable - failed;
                Debug.Log(Prefix + " completed. Discovered=" + ordered.Count + ", successful=" + successful + ", unavailable=" + unavailable + ", failed=" + failed + ". Export directory: " + directory);
            }
            catch (Exception exception)
            {
                Debug.LogError(Prefix + " manifest generation failed after icon extraction. Export directory: " + directory + "\n" + exception);
            }
            finally
            {
                renderSurface?.Dispose();
                renderSurface = null;
            }
        }

        private ExportItem ExportEntry(ItemAssetsCollection.Entry entry, bool duplicateTypeId, string iconDirectory, ISet<string> fileNames)
        {
            var meta = entry.metaData;
            var item = new ExportItem
            {
                TypeId = entry.typeID,
                InternalName = meta.Name ?? string.Empty,
                DisplayNameKey = meta.DisplayNameKey ?? string.Empty,
                DisplayName = SafeDisplayName(meta),
                Quality = meta.quality,
                Category = meta.Catagory ?? string.Empty,
                Tags = ReadTags(meta.tags),
                Caliber = meta.caliber ?? string.Empty,
                Status = ExportStatus.Failed,
                Reason = duplicateTypeId ? "Duplicate base collection entry for this TypeID; exported first entry only." : string.Empty
            };

            try
            {
                // The inventory UI assigns Item.Icon, then uses this exact native fallback only when it is null.
                var actualSprite = entry.prefab != null ? entry.prefab.Icon : meta.icon;
                var usingFallback = actualSprite == null;
                var sprite = actualSprite ?? GameplayDataSettings.UIStyle.FallbackItemIcon;
                if (sprite == null)
                {
                    item.Status = ExportStatus.NoIconAvailable;
                    item.Reason = AppendReason(item.Reason, "Item icon and Duckov native fallback icon are both unavailable.");
                    return item;
                }

                item.SpriteName = sprite.name ?? string.Empty;
                item.TextureName = sprite.texture != null ? sprite.texture.name ?? string.Empty : string.Empty;
                item.Width = Mathf.RoundToInt(sprite.rect.width);
                item.Height = Mathf.RoundToInt(sprite.rect.height);
                if (sprite.texture == null || item.Width <= 0 || item.Height <= 0)
                {
                    item.Status = ExportStatus.Failed;
                    item.Reason = AppendReason(item.Reason, "Sprite has no usable source texture or positive native dimensions.");
                    return item;
                }

                var nameSource = string.IsNullOrEmpty(item.InternalName) ? item.DisplayNameKey : item.InternalName;
                var fileName = ExportNaming.CreateFileName(item.TypeId, nameSource, fileNames);
                var outputPath = ExportNaming.SafeChildPath(iconDirectory, fileName);
                var png = renderSurface!.Render(sprite, item.Width, item.Height);
                if (!PngValidation.HasPngSignature(png)) throw new InvalidOperationException("The Unity PNG encoder returned an invalid PNG signature.");
                File.WriteAllBytes(outputPath, png);
                item.OutputFileName = fileName;
                item.Status = usingFallback ? ExportStatus.NativeFallbackExported : ExportStatus.Exported;
                if (usingFallback) item.Reason = AppendReason(item.Reason, "Item.Icon was null; exported Duckov's native inventory fallback sprite.");
            }
            catch (Exception exception)
            {
                item.Status = ExportStatus.Failed;
                item.Reason = AppendReason(item.Reason, exception.GetType().Name + ": " + exception.Message);
            }
            return item;
        }

        private static string CreateExportDirectory()
        {
            var root = Path.Combine(Application.persistentDataPath, "DuckovItemIconExporter", "exports");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            var directory = Path.Combine(root, timestamp);
            var suffix = 1;
            while (Directory.Exists(directory)) directory = Path.Combine(root, timestamp + "_" + suffix++);
            Directory.CreateDirectory(directory);
            return Path.GetFullPath(directory);
        }

        private static string SafeDisplayName(ItemMetaData meta)
        {
            try { return meta.DisplayName ?? string.Empty; }
            catch (Exception exception) { return "[unavailable: " + exception.GetType().Name + "]"; }
        }
        private static IReadOnlyList<string> ReadTags(Tag[]? tags) { return tags == null ? Array.Empty<string>() : tags.Where(tag => tag != null).Select(tag => tag.name ?? string.Empty).OrderBy(tag => tag, StringComparer.Ordinal).ToArray(); }
        private static string AppendReason(string previous, string next) { return string.IsNullOrEmpty(previous) ? next : previous + " " + next; }
        private static void TryDeleteEmptyDirectory(string directory) { try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch { } }
    }

    internal sealed class SpriteRenderSurface : IDisposable
    {
        private const int RenderLayer = 31;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly SpriteRenderer renderer;

        public SpriteRenderSurface()
        {
            root = new GameObject("DuckovItemIconExporter.RenderSurface") { hideFlags = HideFlags.HideAndDontSave, layer = RenderLayer };
            var cameraObject = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave, layer = RenderLayer };
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            // Render() is invoked explicitly into a temporary RenderTexture. Keeping this camera
            // disabled prevents it from becoming a normal game camera and clearing the menu view.
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << RenderLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var spriteObject = new GameObject("Sprite") { hideFlags = HideFlags.HideAndDontSave, layer = RenderLayer };
            spriteObject.transform.SetParent(root.transform, false);
            renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.color = Color.white;
        }

        public byte[] Render(Sprite sprite, int width, int height)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            var renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D? readable = null;
            var previous = RenderTexture.active;
            try
            {
                renderer.sprite = sprite;
                renderer.transform.localPosition = Vector3.zero;
                var pixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
                camera.aspect = width / (float)height;
                camera.orthographicSize = height / (2f * pixelsPerUnit);
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.EncodeToPNG();
            }
            finally
            {
                renderer.sprite = null;
                camera.targetTexture = null;
                RenderTexture.active = previous;
                if (readable != null) UnityEngine.Object.Destroy(readable);
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
            }
        }

        public void Dispose() { if (root != null) UnityEngine.Object.Destroy(root); }
    }

    internal static class PngValidation
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        public static bool HasPngSignature(byte[]? bytes) { return bytes != null && bytes.Length >= Signature.Length && Signature.SequenceEqual(bytes.Take(Signature.Length)); }
    }
}
