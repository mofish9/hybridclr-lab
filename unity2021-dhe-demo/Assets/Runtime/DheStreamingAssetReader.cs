using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace HybridCLR.Lab
{
    /// <summary>
    /// Reads DHE control files and payloads from the same logical
    /// StreamingAssets path on desktop/iOS and inside an Android APK.
    /// Project resource providers can replace this with YooAsset/Addressables;
    /// the demo keeps the platform boundary explicit for smoke coverage.
    /// </summary>
    internal static class DheStreamingAssetReader
    {
        public static bool Exists(string relativePath)
        {
            string normalized = Normalize(relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (FileStream apk = File.OpenRead(Application.dataPath))
            using (ZipArchive archive = new ZipArchive(apk, ZipArchiveMode.Read, false))
            {
                return archive.GetEntry(GetAndroidEntryName(normalized)) != null;
            }
#else
            return File.Exists(ResolveFilePath(normalized));
#endif
        }

        public static byte[] Read(string relativePath)
        {
            string normalized = Normalize(relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (FileStream apk = File.OpenRead(Application.dataPath))
            using (ZipArchive archive = new ZipArchive(apk, ZipArchiveMode.Read, false))
            {
                ZipArchiveEntry entry = archive.GetEntry(GetAndroidEntryName(normalized));
                if (entry == null)
                    throw new FileNotFoundException("DHE StreamingAssets entry was not found in APK.",
                        normalized);
                using (Stream input = entry.Open())
                using (MemoryStream output = new MemoryStream(checked((int)entry.Length)))
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
#else
            return File.ReadAllBytes(ResolveFilePath(normalized));
#endif
        }

        private static string Normalize(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("DHE StreamingAssets path must be relative.");
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            if (normalized.Split('/').Any(segment => segment.Length == 0 || segment == "." ||
                segment == ".."))
                throw new InvalidDataException("DHE StreamingAssets path is unsafe: " + relativePath);
            return normalized;
        }

        private static string ResolveFilePath(string normalized)
        {
            string root = Path.GetFullPath(Application.streamingAssetsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DHE StreamingAssets path escapes its root.");
            return path;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static string GetAndroidEntryName(string normalized)
        {
            string streamingAssetsPath = Application.streamingAssetsPath.Replace('\\', '/');
            int separator = streamingAssetsPath.IndexOf("!/", StringComparison.Ordinal);
            if (separator < 0)
                throw new InvalidDataException("Unexpected Android StreamingAssets path: " +
                    streamingAssetsPath);
            string entryRoot = streamingAssetsPath.Substring(separator + 2).Trim('/');
            return entryRoot.Length == 0 ? normalized : entryRoot + "/" + normalized;
        }
#endif
    }
}
