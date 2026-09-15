#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Styx.Helpers;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Manages protected items that should not be sold, mailed, or destroyed.
    /// </summary>
    public static class ProtectedItemsManager
    {
        // Items from XML files
        private static DualHashSet<uint, string> _fileProtectedItems;
        private static readonly object _reloadSync = new object();
        private static DualHashSet<uint, string> FileSnapshot => Volatile.Read(ref _fileProtectedItems);
        // Items added at runtime
        private static readonly DualHashSet<uint, string> _runtimeProtectedItems;
        // Legacy Add/Remove own the manual set; collector leases are independent.
        private static readonly object _runtimeSync = new object();
        private static readonly Dictionary<uint, int> _itemOwners = new Dictionary<uint, int>();
        // Valid file names for protected items
        private static readonly HashSet<string> _validFileNames;

        static ProtectedItemsManager()
        {
            _fileProtectedItems = new DualHashSet<uint, string>();
            _runtimeProtectedItems = new DualHashSet<uint, string>();
            _validFileNames = new HashSet<string>
            {
                "protected items",
                "protecteditems",
                "protitems"
            };
            ReloadProtectedItems();
        }

        /// <summary>
        /// Reloads protected items from XML files.
        /// </summary>
        public static void ReloadProtectedItems()
        {
            Exception failure = null;
            string source = null;
            lock (_reloadSync)
            {
                // Build privately. Readers continue using the old complete FILE
                // layer while I/O or parsing is in progress; runtime owners are separate.
                var candidate = new DualHashSet<uint, string>();
                try
                {
                    source = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (source == null) return;
                    string[] files = Directory.GetFiles(source, "*.xml", SearchOption.AllDirectories);
                    foreach (string filePath in files)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath)?.ToLowerInvariant();
                        if (!_validFileNames.Contains(fileName)) continue;
                        source = filePath;
                        AppendProtectedItemsFile(filePath, candidate);
                    }
                    Volatile.Write(ref _fileProtectedItems, candidate);
                }
                catch (Exception error) when (error is XmlException || error is IOException
                    || error is UnauthorizedAccessException)
                {
                    // Never publish a partial candidate or roll back a newer layer.
                    failure = error;
                }
            }
            // Logging is a callback boundary: run it outside manager locks. A
            // callback may reload successfully or throw a stop signal of its own.
            if (failure != null)
                Logging.Write($"Error in ProtectedItems XML file at {source}: {failure.Message}");
        }

        /// <summary>
        /// Parses one enumerated file into an unpublished candidate FILE layer.
        /// Read/parse failure aborts the caller's complete reload, not just this file.
        /// </summary>
        private static void AppendProtectedItemsFile(string filePath, DualHashSet<uint, string> candidate)
        {
            // A file disappearing after enumeration is an incomplete observation.
            // Do not silently skip it through a File.Exists check.
            XElement root = XElement.Load(filePath);

            foreach (var element in root.Elements())
            {
                if (element.Name.ToString().ToLowerInvariant() != "item")
                    continue;

                uint id = 0;
                string name = "";

                foreach (var attr in element.Attributes())
                {
                    string attrName = attr.Name.ToString().ToLowerInvariant();
                    switch (attrName)
                    {
                        case "id":
                        case "entry":
                            uint.TryParse(attr.Value, out id);
                            break;
                        case "name":
                            name = attr.Value.ToLowerInvariant();
                            break;
                    }
                }

                // Try to get id from element value if not in attributes
                if (id == 0 && !string.IsNullOrEmpty(element.Value))
                {
                    uint.TryParse(element.Value, out id);
                }

                if (id == 0 && string.IsNullOrEmpty(name))
                    continue;

                if (!candidate.Contains(id))
                    candidate.Add(id);
                if (!candidate.Contains(name))
                    candidate.Add(name);
            }
        }

        /// <summary>
        /// Checks if an item id is protected.
        /// </summary>
        public static bool Contains(uint item)
        {
            if (FileSnapshot.Contains(item))
                return true;
            lock (_runtimeSync)
                if (_runtimeProtectedItems.Contains(item) || _itemOwners.ContainsKey(item))
                    return true;

            return CheckProfileProtectedItems(item);
        }

        /// <summary>
        /// Checks if an item name is protected.
        /// </summary>
        public static bool Contains(string item)
        {
            item = item.ToLowerInvariant();

            if (FileSnapshot.Contains(item))
                return true;
            lock (_runtimeSync)
                if (_runtimeProtectedItems.Contains(item))
                    return true;

            return CheckProfileProtectedItems(item);
        }

        /// <summary>
        /// Checks if item is in current profile's protected items.
        /// </summary>
        private static bool CheckProfileProtectedItems(uint item)
        {
            return ProfileManager.CurrentProfile?.ProtectedItems?.Contains(item) ?? false;
        }

        /// <summary>
        /// Checks if item is in current profile's protected items.
        /// </summary>
        private static bool CheckProfileProtectedItems(string item)
        {
            return ProfileManager.CurrentProfile?.ProtectedItems?.Contains(item.ToLowerInvariant()) ?? false;
        }

        /// <summary>
        /// Adds an item id to the runtime protected list.
        /// </summary>
        public static bool Add(uint item) { lock (_runtimeSync) return _runtimeProtectedItems.Add(item); }

        /// <summary>
        /// Adds an item name to the runtime protected list.
        /// </summary>
        public static bool Add(string item) { lock (_runtimeSync) return _runtimeProtectedItems.Add(item.ToLowerInvariant()); }

        /// <summary>
        /// Removes an item id from the runtime protected list.
        /// </summary>
        public static bool Remove(uint item) { lock (_runtimeSync) return _runtimeProtectedItems.Remove(item); }

        /// <summary>
        /// Removes an item name from the runtime protected list.
        /// </summary>
        public static bool Remove(string item) { lock (_runtimeSync) return _runtimeProtectedItems.Remove(item.ToLowerInvariant()); }

        /// <summary>
        /// Acquires one runtime item-ID owner. Disposing it releases only this
        /// owner, never FILE/profile/manual protection or another active lease.
        /// </summary>
        public static IDisposable Acquire(uint item)
        {
            var owner = new ItemProtection(item);
            lock (_runtimeSync)
            {
                _itemOwners.TryGetValue(item, out int count);
                _itemOwners[item] = checked(count + 1);
            }
            return owner;
        }

        private sealed class ItemProtection : IDisposable
        {
            private readonly uint item;
            private int disposed;
            internal ItemProtection(uint item) { this.item = item; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                lock (_runtimeSync)
                {
                    int count = _itemOwners[item];
                    if (count == 1) _itemOwners.Remove(item);
                    else _itemOwners[item] = count - 1;
                }
            }
        }

        /// <summary>
        /// Gets all protected item names.
        /// </summary>
        public static List<string> GetAllItemNames()
        {
            var list = new List<string>();

            foreach (string name in FileSnapshot.HashSet2)
                list.Add(name);

            lock (_runtimeSync)
                list.AddRange(_runtimeProtectedItems.HashSet2);

            if (ProfileManager.CurrentProfile?.ProtectedItems != null)
            {
                foreach (string name in ProfileManager.CurrentProfile.ProtectedItems.HashSet2)
                    list.Add(name);
            }

            return list;
        }

        /// <summary>
        /// Gets all protected item ids.
        /// </summary>
        public static List<uint> GetAllItemIds()
        {
            var list = new List<uint>();

            foreach (uint id in FileSnapshot.HashSet1)
                list.Add(id);

            lock (_runtimeSync)
            {
                list.AddRange(_runtimeProtectedItems.HashSet1);
                foreach (uint id in _itemOwners.Keys)
                    if (!_runtimeProtectedItems.Contains(id)) list.Add(id);
            }

            if (ProfileManager.CurrentProfile?.ProtectedItems != null)
            {
                foreach (uint id in ProfileManager.CurrentProfile.ProtectedItems.HashSet1)
                    list.Add(id);
            }

            return list;
        }
    }
}
