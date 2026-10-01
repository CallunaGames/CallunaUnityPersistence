using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// Stores game data backups as one JSON file each in a directory, and keeps their number bounded.
    /// <para>
    /// Retention: per save version the newest <see cref="Settings.BackupsPerVersion"/> backups, plus the
    /// newest backup of the build before the latest one - so a buggy release that doesn't change the save
    /// version can't rotate away the last backup of the build before it. Only the newest
    /// <see cref="Settings.VersionsKept"/> save versions up to the current one are kept; backups of a newer
    /// save version (from a newer game, after a downgrade) are never deleted.
    /// </para>
    /// </summary>
    internal class GameDataBackupStore
    {
        private const string FilePrefix = "backup_v";
        private const string FileExtension = ".json";

        private readonly Settings _settings;
        private readonly object _lock = new object();
        private Task _pendingWrite = Task.CompletedTask;

        internal GameDataBackupStore(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Backs up <paramref name="data"/>. Serializes on the calling thread (so later changes to the
        /// tokens don't matter), writes and rotates on a background thread.
        /// </summary>
        internal Task CreateAsync(IReadOnlyList<(int SaveVersion, Dictionary<string, JToken> Data)> backups,
            int currentVersion, string gameVersion, DateTime createdUtc)
        {
            List<(string Path, string Content)> files = new List<(string, string)>(backups.Count);
            foreach ((int saveVersion, Dictionary<string, JToken> data) in backups)
            {
                JObject content = new JObject
                {
                    ["SaveVersion"] = saveVersion,
                    ["CreatedUtc"] = createdUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["GameVersion"] = gameVersion ?? string.Empty,
                    ["Data"] = new JObject(data.Select(pair => new JProperty(pair.Key, pair.Value.DeepClone()))),
                };
                string fileName = $"{FilePrefix}{saveVersion}_{createdUtc:yyyyMMdd'T'HHmmssfff}{FileExtension}";
                files.Add((Path.Combine(_settings.Directory, fileName), content.ToString(Formatting.None)));
            }

            lock (_lock)
            {
                _pendingWrite = _pendingWrite.ContinueWith(_ => WriteAndRotate(files, currentVersion),
                    TaskScheduler.Default);
                return _pendingWrite;
            }
        }

        /// <summary>Blocks until backups being written in the background are done.</summary>
        internal void FlushPendingWrite()
        {
            Task pending;
            lock (_lock)
                pending = _pendingWrite;
            pending.Wait();
        }

        /// <summary>All readable backups, newest first.</summary>
        internal List<GameDataBackup> List()
        {
            List<GameDataBackup> backups = new List<GameDataBackup>();
            if (!Directory.Exists(_settings.Directory))
                return backups;
            foreach (string path in Directory.GetFiles(_settings.Directory, FilePrefix + "*" + FileExtension))
            {
                if (TryReadInfo(path, out GameDataBackup backup))
                    backups.Add(backup);
            }
            backups.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            return backups;
        }

        /// <summary>Reads the data of <paramref name="backup"/>. Throws if the file can't be read.</summary>
        internal Dictionary<string, JToken> ReadData(GameDataBackup backup)
        {
            JObject content = JObject.Parse(File.ReadAllText(backup.FilePath));
            if (content["Data"] is not JObject data)
                throw new InvalidDataException($"The backup '{backup.FilePath}' contains no data.");
            return data.Properties().ToDictionary(property => property.Name, property => property.Value);
        }

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------

        private void WriteAndRotate(List<(string Path, string Content)> files, int currentVersion)
        {
            try
            {
                Directory.CreateDirectory(_settings.Directory);
                foreach ((string path, string content) in files)
                {
                    // Written next to the target and moved into place, so quitting mid-write never
                    // leaves a truncated backup behind.
                    string temporaryPath = path + ".tmp";
                    File.WriteAllText(temporaryPath, content);
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temporaryPath, path);
                }
                Rotate(currentVersion);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to write a game data backup to '{_settings.Directory}': {e}");
            }
        }

        private void Rotate(int currentVersion)
        {
            List<GameDataBackup> backups = List();
            List<IGrouping<int, GameDataBackup>> versions = backups
                .Where(backup => backup.SaveVersion <= currentVersion)
                .GroupBy(backup => backup.SaveVersion)
                .OrderByDescending(group => group.Key)
                .ToList();

            for (int i = 0; i < versions.Count; i++)
            {
                IEnumerable<GameDataBackup> toDelete = i < _settings.VersionsKept
                    ? versions[i].Except(Retained(versions[i]))
                    : versions[i];
                foreach (GameDataBackup backup in toDelete)
                    File.Delete(backup.FilePath);
            }
        }

        // The newest BackupsPerVersion, plus the newest backup of the build before the latest one.
        private HashSet<GameDataBackup> Retained(IEnumerable<GameDataBackup> versionBackups)
        {
            List<GameDataBackup> newestFirst = versionBackups.OrderByDescending(backup => backup.CreatedUtc).ToList();
            HashSet<GameDataBackup> retained = new HashSet<GameDataBackup>(newestFirst.Take(_settings.BackupsPerVersion));
            string latestBuild = newestFirst[0].GameVersion;
            GameDataBackup previousBuild = newestFirst.FirstOrDefault(backup => backup.GameVersion != latestBuild);
            if (previousBuild != null)
                retained.Add(previousBuild);
            return retained;
        }

        private static bool TryReadInfo(string path, out GameDataBackup backup)
        {
            try
            {
                using StreamReader file = File.OpenText(path);
                using JsonTextReader reader = new JsonTextReader(file);
                int? saveVersion = null;
                DateTime? createdUtc = null;
                string gameVersion = null;
                // Only the header properties, which come before the data.
                reader.Read();
                while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
                {
                    string name = (string)reader.Value;
                    if (name == "Data")
                        break;
                    reader.Read();
                    switch (name)
                    {
                        case "SaveVersion": saveVersion = Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture); break;
                        case "CreatedUtc": createdUtc = ParseUtc(reader.Value); break;
                        case "GameVersion": gameVersion = (string)reader.Value; break;
                    }
                }

                if (saveVersion == null || createdUtc == null)
                {
                    backup = null;
                    return false;
                }
                backup = new GameDataBackup(saveVersion.Value, createdUtc.Value, gameVersion ?? string.Empty, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Skipping unreadable game data backup '{path}': {e.Message}");
                backup = null;
                return false;
            }
        }

        // Newtonsoft may already parse the ISO date into a DateTime.
        private static DateTime ParseUtc(object value) => value is DateTime dateTime
            ? dateTime.ToUniversalTime()
            : DateTime.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

        internal class Settings
        {
            /// <summary>Absolute directory the backups are stored in.</summary>
            public string Directory;
            public int BackupsPerVersion = 3;
            public int VersionsKept = 3;
        }
    }
}
