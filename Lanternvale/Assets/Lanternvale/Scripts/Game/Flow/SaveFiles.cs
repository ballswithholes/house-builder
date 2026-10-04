// Save slots on disk: Application.persistentDataPath/saves/<slot>.json.
// Writes are atomic (temp file + replace) so a crash mid-save never corrupts an existing slot.
// Headers (name, class, level, map, day/hour, play time) are parsed once per file version and cached,
// so save-slot screens may call GameFlow.ListSaves() every frame.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    internal static class SaveFiles
    {
        const string Extension = ".json";
        const float ListCacheSeconds = 1.0f;

        sealed class CacheEntry
        {
            public DateTime Modified;
            public long Length;
            public SaveSlotInfo Info;
        }

        static readonly Dictionary<string, CacheEntry> headerCache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly List<SaveSlotInfo> cachedList = new List<SaveSlotInfo>();
        static float cachedAt = -1000f;
        static bool dirty = true;

        static string directory;

        /// <summary>Folder holding the save files (created on first write).</summary>
        public static string Directory
        {
            get
            {
                if (string.IsNullOrEmpty(directory)) directory = Path.Combine(Application.persistentDataPath, "saves");
                return directory;
            }
        }

        /// <summary>Normalised slot name: letters, digits, '-' and '_' only (lower case), "" if nothing is left.</summary>
        public static string CleanSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return "";
            var sb = new StringBuilder(slot.Length);
            foreach (var ch in slot.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(char.ToLowerInvariant(ch));
                else if (ch == ' ') sb.Append('_');
            }
            if (sb.Length > 48) sb.Length = 48;
            return sb.ToString();
        }

        public static string PathOf(string slot) => Path.Combine(Directory, CleanSlot(slot) + Extension);

        public static bool Exists(string slot)
        {
            var s = CleanSlot(slot);
            if (s.Length == 0) return false;
            try { return File.Exists(PathOf(s)); }
            catch (Exception) { return false; }
        }

        /// <summary>Writes a slot atomically. Returns null on success or an error message.</summary>
        public static string Write(string slot, string json)
        {
            var s = CleanSlot(slot);
            if (s.Length == 0) return "Invalid save slot name.";
            if (string.IsNullOrEmpty(json)) return "Nothing to save.";
            string path = PathOf(s), tmp = path + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null); }
                    catch (Exception)
                    {
                        // some platforms/file systems do not support Replace: fall back to delete + move
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else File.Move(tmp, path);
                Invalidate();
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lanternvale] Saving slot '{s}' failed: {e}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                Invalidate();
                return "Could not write the save file: " + e.Message;
            }
        }

        /// <summary>Reads a slot. Returns null on success (json set) or an error message.</summary>
        public static string Read(string slot, out string json)
        {
            json = null;
            var s = CleanSlot(slot);
            if (s.Length == 0) return "Invalid save slot name.";
            var path = PathOf(s);
            try
            {
                if (!File.Exists(path)) return $"There is no save in slot '{DisplaySlot(s)}'.";
                json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(json)) return "The save file is empty.";
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lanternvale] Reading slot '{s}' failed: {e}");
                return "Could not read the save file: " + e.Message;
            }
        }

        public static string Delete(string slot)
        {
            var s = CleanSlot(slot);
            if (s.Length == 0) return "Invalid save slot name.";
            try
            {
                var path = PathOf(s);
                if (!File.Exists(path)) return $"There is no save in slot '{DisplaySlot(s)}'.";
                File.Delete(path);
                Invalidate();
                return null;
            }
            catch (Exception e)
            {
                Invalidate();
                return "Could not delete the save: " + e.Message;
            }
        }

        /// <summary>Every save on disk, newest first (cached for a second; refreshed after writes/deletes).</summary>
        public static List<SaveSlotInfo> List()
        {
            Refresh();
            return new List<SaveSlotInfo>(cachedList);
        }

        public static bool HasAny()
        {
            Refresh();
            foreach (var i in cachedList) if (i.Header != null) return true;
            return false;
        }

        /// <summary>Newest readable save, or null.</summary>
        public static SaveSlotInfo Latest()
        {
            Refresh();
            foreach (var i in cachedList) if (i.Header != null && string.IsNullOrEmpty(i.Error)) return i;
            return null;
        }

        public static void Invalidate() { dirty = true; }

        /// <summary>"auto" → "Autosave", "quick" → "Quicksave", "slot3" → "Slot 3".</summary>
        public static string DisplaySlot(string slot)
        {
            switch (slot)
            {
                case "auto": return "Autosave";
                case "quick": return "Quicksave";
            }
            if (slot != null && slot.StartsWith("slot", StringComparison.Ordinal) && slot.Length > 4) return "Slot " + slot.Substring(4);
            return slot ?? "";
        }

        static void Refresh()
        {
            float now = Time.realtimeSinceStartup;
            if (!dirty && now - cachedAt < ListCacheSeconds) return;
            dirty = false;
            cachedAt = now;
            cachedList.Clear();
            string[] files;
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return;
                files = System.IO.Directory.GetFiles(Directory, "*" + Extension);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Lanternvale] Listing saves failed: " + e.Message);
                return;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                seen.Add(path);
                var info = ReadInfo(path);
                if (info != null) cachedList.Add(info);
            }
            // forget cache entries of deleted files
            if (headerCache.Count > seen.Count)
            {
                var gone = new List<string>();
                foreach (var k in headerCache.Keys) if (!seen.Contains(k)) gone.Add(k);
                foreach (var k in gone) headerCache.Remove(k);
            }
            cachedList.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        }

        static SaveSlotInfo ReadInfo(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                var modified = fi.LastWriteTime;
                long length = fi.Length;
                if (headerCache.TryGetValue(path, out var c) && c.Modified == modified && c.Length == length) return c.Info;
                var info = new SaveSlotInfo { Slot = Path.GetFileNameWithoutExtension(path), Modified = modified };
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    info.Header = GameSession.ReadSaveHeader(json);
                    if (info.Header == null || info.Header.version <= 0 || string.IsNullOrEmpty(info.Header.mapId))
                    {
                        info.Header = null;
                        info.Error = "Unreadable save file.";
                    }
                    else if (info.Header.version > GameSession.SaveVersion)
                        info.Error = "Made by a newer version of the game.";
                }
                catch (Exception e)
                {
                    info.Header = null;
                    info.Error = "Could not read the save file: " + e.Message;
                }
                headerCache[path] = new CacheEntry { Modified = modified, Length = length, Info = info };
                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
