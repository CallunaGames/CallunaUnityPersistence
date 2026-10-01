using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Calluna.DI;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Calluna.Persistence.Tests
{
    /// <summary>
    /// Tests for the game data backups: created by <see cref="GameDataPersistence.Load"/> after a
    /// successful load, rotated by <see cref="GameDataBackupStore"/>, restored by
    /// <see cref="GameDataPersistence.RestoreBackup"/>.
    /// </summary>
    [TestFixture]
    public class GameDataBackupTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"CallunaBackupTest_{Guid.NewGuid():N}");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        // -----------------------------------------------------------------------
        // Test doubles
        // -----------------------------------------------------------------------

        private class MemoryStore : SaveLoader
        {
            public event Action OnClear;
            private readonly object _lock = new object();
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public bool Has(string id) { lock (_lock) return _store.ContainsKey(id); }

            public T Load<T>(string id, T defaultValue = default)
            {
                lock (_lock)
                    return _store.TryGetValue(id, out string json)
                        ? Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json)
                        : defaultValue;
            }

            public void Save<T>(string id, T value)
            {
                lock (_lock)
                    _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            }

            public void Delete(string id) { lock (_lock) _store.Remove(id); }
            public void Clear() { lock (_lock) _store.Clear(); OnClear?.Invoke(); }
        }

        private class Loader : IDataSaveLoader
        {
            public string DataId { get; }
            public bool Throw;
            public JToken Loaded;
            public JToken Value = JToken.FromObject(0);

            public Loader(string id) => DataId = id;

            public void Load(JToken value)
            {
                if (Throw)
                    throw new InvalidOperationException("Simulated load failure");
                Loaded = value;
            }

            public void LoadDefault() => Loaded = null;
            public JToken GetSerializedData() => Value;
        }

        private class AddingMigrator : IGameDataMigrator
        {
            public int Version { get; }
            public AddingMigrator(int version) => Version = version;
            public void Migrate(Dictionary<string, JToken> data) => data["a"] = JToken.FromObject(data["a"].Value<int>() + 100);
        }

        // -----------------------------------------------------------------------
        // Infrastructure
        // -----------------------------------------------------------------------

        private GameDataBackupStore.Settings Settings(int perVersion = 3, int versionsKept = 3) =>
            new GameDataBackupStore.Settings { Directory = _directory, BackupsPerVersion = perVersion, VersionsKept = versionsKept };

        private GameDataPersistence BuildPersistence(SaveLoader store, int minVersion, int currentVersion,
            IReadOnlyList<IDataSaveLoader> loaders, IReadOnlyList<IGameDataMigrator> migrators = null,
            GameDataBackupStore.Settings backups = null, bool backupsEnabled = true)
        {
            Mock<Resolver> serializerResolver = new Mock<Resolver>();
            serializerResolver.Setup(x => x.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            serializerResolver.Setup(x => x.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);
            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(serializerResolver.Object);

            Mock<Resolver> writerResolver = new Mock<Resolver>();
            writerResolver.Setup(x => x.Resolve<SaveLoader>()).Returns(store);
            writerResolver.Setup(x => x.Resolve<GameDataWriter.Arguments>())
                .Returns(new GameDataWriter.Arguments { GameDataId = "backup_test", CurrentVersion = currentVersion });
            GameDataWriter writer = new GameDataWriter();
            ((Injectable)writer).Inject(writerResolver.Object);

            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<SaveLoader>()).Returns(store);
            r.Setup(x => x.Resolve<JsonSerializer>()).Returns(serializer);
            r.Setup(x => x.Resolve<GameDataWriter>()).Returns(writer);
            r.Setup(x => x.Resolve<GameDataPersistence.Arguments>()).Returns(new GameDataPersistence.Arguments
            {
                GameDataId = "backup_test",
                MinSupportedVersion = minVersion,
                CurrentVersion = currentVersion,
                SaveLoaders = loaders,
                Migrators = migrators,
                Backups = backupsEnabled ? backups ?? Settings() : null,
            });

            GameDataPersistence persistence = new GameDataPersistence();
            ((Injectable)persistence).Inject(r.Object);
            ((Initializable)persistence).Initialize();
            return persistence;
        }

        private static MemoryStore StoreWith(int version, int a)
        {
            MemoryStore store = new MemoryStore();
            store.Save(GameDataPersistence.VersionKey, version);
            store.Save("a", JToken.FromObject(a));
            return store;
        }

        private static void LoadAndWait(GameDataPersistence persistence)
        {
            persistence.Load();
            persistence.FlushPendingWrite();
        }

        private GameDataBackupStore Store(int perVersion = 3, int versionsKept = 3) =>
            new GameDataBackupStore(Settings(perVersion, versionsKept));

        private static readonly DateTime T0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        private static void Create(GameDataBackupStore store, int saveVersion, int currentVersion, int minute, string build)
        {
            store.CreateAsync(new[] { (saveVersion, new Dictionary<string, JToken> { ["a"] = JToken.FromObject(minute) }) },
                currentVersion, build, T0.AddMinutes(minute)).Wait();
        }

        // -----------------------------------------------------------------------
        // Creating backups
        // -----------------------------------------------------------------------

        [Test]
        [Description("A successful load => a backup of the loaded data at its save version.")]
        public void Load_Success_CreatesBackup()
        {
            GameDataPersistence persistence = BuildPersistence(StoreWith(1, 7), 0, 1, new IDataSaveLoader[] { new Loader("a") });

            LoadAndWait(persistence);

            IReadOnlyList<GameDataBackup> backups = persistence.GetBackups();
            Assert.That(backups.Count, Is.EqualTo(1));
            Assert.That(backups[0].SaveVersion, Is.EqualTo(1));
            Assert.That(backups[0].GameVersion, Is.EqualTo(Application.version));
            Assert.That(Store().ReadData(backups[0])["a"].Value<int>(), Is.EqualTo(7));
        }

        [Test]
        [Description("No save yet => nothing to back up.")]
        public void Load_NoSave_CreatesNoBackup()
        {
            GameDataPersistence persistence = BuildPersistence(new MemoryStore(), 0, 1, new IDataSaveLoader[] { new Loader("a") });

            LoadAndWait(persistence);

            Assert.That(persistence.GetBackups(), Is.Empty);
        }

        [Test]
        [Description("A failed load => no backup created and none removed - the existing ones stay unchanged.")]
        public void Load_Failed_KeepsExistingBackupsUnchanged()
        {
            MemoryStore store = StoreWith(1, 7);
            LoadAndWait(BuildPersistence(store, 0, 1, new IDataSaveLoader[] { new Loader("a") }));
            GameDataBackup existing = BuildPersistence(store, 0, 1, new IDataSaveLoader[0]).GetBackups().Single();
            string before = File.ReadAllText(existing.FilePath);

            GameDataPersistence failing = BuildPersistence(store, 0, 1, new IDataSaveLoader[] { new Loader("a") { Throw = true } },
                backups: Settings(perVersion: 1));
            LogAssert.Expect(LogType.Error, new Regex("Failed to load game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            LoadAndWait(failing);

            Assert.That(failing.GetBackups().Count, Is.EqualTo(1));
            Assert.That(File.ReadAllText(existing.FilePath), Is.EqualTo(before));
        }

        [Test]
        [Description("A load that migrates => backups of the migrated data and of the data before the migration, " +
                     "each at its own save version.")]
        public void Load_WithMigration_BacksUpBeforeAndAfter()
        {
            GameDataPersistence persistence = BuildPersistence(StoreWith(0, 7), 0, 1,
                new IDataSaveLoader[] { new Loader("a") }, new IGameDataMigrator[] { new AddingMigrator(1) });

            LoadAndWait(persistence);

            Dictionary<int, GameDataBackup> byVersion = persistence.GetBackups().ToDictionary(b => b.SaveVersion);
            Assert.That(byVersion.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(Store().ReadData(byVersion[0])["a"].Value<int>(), Is.EqualTo(7), "Before the migration.");
            Assert.That(Store().ReadData(byVersion[1])["a"].Value<int>(), Is.EqualTo(107), "After the migration.");
        }

        [Test]
        [Description("Backups disabled => none created, GetBackups empty, RestoreBackup throws.")]
        public void Disabled_NoBackups()
        {
            GameDataPersistence persistence = BuildPersistence(StoreWith(1, 7), 0, 1,
                new IDataSaveLoader[] { new Loader("a") }, backupsEnabled: false);

            LoadAndWait(persistence);

            Assert.That(persistence.GetBackups(), Is.Empty);
            Assert.That(Directory.Exists(_directory), Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                persistence.RestoreBackup(new GameDataBackup(1, T0, "x", Path.Combine(_directory, "none.json"))));
        }

        // -----------------------------------------------------------------------
        // Rotation
        // -----------------------------------------------------------------------

        [Test]
        [Description("More backups of a save version than kept => the newest ones stay.")]
        public void Rotation_KeepsNewestPerVersion()
        {
            GameDataBackupStore store = Store(perVersion: 2);
            for (int minute = 1; minute <= 4; minute++)
                Create(store, 5, 5, minute, "1.0");

            List<GameDataBackup> backups = store.List();

            Assert.That(backups.Select(b => b.CreatedUtc), Is.EqualTo(new[] { T0.AddMinutes(4), T0.AddMinutes(3) }));
        }

        [Test]
        [Description("A new build fills the save version's slots => the newest backup of the previous build is kept " +
                     "in addition, so a buggy build that doesn't change the save version can't rotate it away.")]
        public void Rotation_KeepsNewestBackupOfPreviousBuild()
        {
            GameDataBackupStore store = Store(perVersion: 2);
            Create(store, 5, 5, 1, "1.0");
            Create(store, 5, 5, 2, "1.0");
            Create(store, 5, 5, 3, "1.1");
            Create(store, 5, 5, 4, "1.1");
            Create(store, 5, 5, 5, "1.1");

            List<GameDataBackup> backups = store.List();

            Assert.That(backups.Select(b => b.CreatedUtc),
                Is.EqualTo(new[] { T0.AddMinutes(5), T0.AddMinutes(4), T0.AddMinutes(2) }));
            Assert.That(backups.Last().GameVersion, Is.EqualTo("1.0"));
        }

        [Test]
        [Description("More save versions than kept => the oldest versions' backups go; backups of a newer save " +
                     "version than the current one (from a newer game) are never deleted.")]
        public void Rotation_KeepsNewestVersionsAndNeverNewerOnes()
        {
            GameDataBackupStore store = Store(versionsKept: 2);
            Create(store, 7, 7, 1, "2.0");  // newer than the current version below
            Create(store, 3, 5, 2, "1.0");
            Create(store, 4, 5, 3, "1.0");
            Create(store, 5, 5, 4, "1.0");

            List<int> versions = store.List().Select(b => b.SaveVersion).OrderBy(v => v).ToList();

            Assert.That(versions, Is.EqualTo(new[] { 4, 5, 7 }));
        }

        [Test]
        [Description("An unreadable file in the backup folder => skipped when listing, not deleted.")]
        public void List_UnreadableFile_SkippedAndKept()
        {
            GameDataBackupStore store = Store();
            Create(store, 5, 5, 1, "1.0");
            string broken = Path.Combine(_directory, "backup_v5_broken.json");
            File.WriteAllText(broken, "{ not json");

            LogAssert.Expect(LogType.Warning, new Regex("Skipping unreadable game data backup"));
            List<GameDataBackup> backups = store.List();

            Assert.That(backups.Count, Is.EqualTo(1));
            Assert.That(File.Exists(broken), Is.True);
        }

        // -----------------------------------------------------------------------
        // Restoring
        // -----------------------------------------------------------------------

        [Test]
        [Description("RestoreBackup => the stored data is replaced by the backup's (data the backup lacks is removed, " +
                     "the version is the backup's), and the backup file stays unchanged.")]
        public void RestoreBackup_ReplacesStoredDataAndKeepsBackup()
        {
            MemoryStore store = StoreWith(1, 7);
            LoadAndWait(BuildPersistence(store, 0, 2, new IDataSaveLoader[] { new Loader("a") }, new IGameDataMigrator[] { new AddingMigrator(2) }));
            store.Save("a", JToken.FromObject(-1));
            store.Save("b", JToken.FromObject(-1));
            GameDataPersistence persistence = BuildPersistence(store, 0, 2, new IDataSaveLoader[] { new Loader("a"), new Loader("b") });
            GameDataBackup backup = persistence.GetBackups().Single(b => b.SaveVersion == 1);
            string before = File.ReadAllText(backup.FilePath);

            persistence.RestoreBackup(backup);

            Assert.That(store.Load<int>("a"), Is.EqualTo(7));
            Assert.That(store.Has("b"), Is.False, "Data the backup doesn't have must be removed.");
            Assert.That(store.Load<int>(GameDataPersistence.VersionKey), Is.EqualTo(1));
            Assert.That(File.ReadAllText(backup.FilePath), Is.EqualTo(before));
        }

        [Test]
        [Description("After RestoreBackup => saving is blocked until the next Load, so the replaced in-memory state " +
                     "can't be written over the restored data. Loading then migrates the restored data as usual.")]
        public void RestoreBackup_BlocksSavingUntilNextLoad()
        {
            MemoryStore store = StoreWith(1, 7);
            Loader loader = new Loader("a") { Value = JToken.FromObject(-5) };
            GameDataPersistence persistence = BuildPersistence(store, 0, 2, new IDataSaveLoader[] { loader }, new IGameDataMigrator[] { new AddingMigrator(2) });
            LoadAndWait(persistence);
            GameDataBackup backup = persistence.GetBackups().Single(b => b.SaveVersion == 1);

            persistence.RestoreBackup(backup);
            LogAssert.Expect(LogType.Warning, new Regex("Save was skipped: a backup was restored"));
            persistence.Save();

            Assert.That(store.Load<int>("a"), Is.EqualTo(7), "The restored data must not be overwritten.");

            persistence.Load();
            Assert.That(loader.Loaded.Value<int>(), Is.EqualTo(107), "Restored data is migrated on load.");
            persistence.Save();
            Assert.That(store.Load<int>("a"), Is.EqualTo(-5), "Saving works again after the load.");
        }

        [Test]
        [Description("RestoreBackup with a save version this game can't load => ArgumentException, nothing changed.")]
        public void RestoreBackup_UnsupportedVersion_Throws()
        {
            MemoryStore store = StoreWith(1, 7);
            GameDataPersistence persistence = BuildPersistence(store, 1, 1, new IDataSaveLoader[] { new Loader("a") });
            GameDataBackupStore backups = Store();
            Create(backups, 3, 3, 1, "2.0");
            GameDataBackup newer = backups.List().Single();

            Assert.That(persistence.GetBackups(), Is.Empty, "Newer save versions aren't offered.");
            Assert.Throws<ArgumentException>(() => persistence.RestoreBackup(newer));
            Assert.That(store.Load<int>("a"), Is.EqualTo(7));
        }
    }
}
