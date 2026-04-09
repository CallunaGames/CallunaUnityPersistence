using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Calluna.DI;
using Calluna.Persistence;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Tests for <see cref="GameDataPersistence"/>.
    ///
    /// <see cref="IDataSaveLoader"/> and <see cref="IGameDataMigrator"/> are plain interfaces,
    /// so test doubles are simple C# classes — no MonoBehaviour/GameObject overhead.
    /// </summary>
    [TestFixture]
    public class GameDataPersistenceTests
    {
        // -----------------------------------------------------------------------
        // Test doubles
        // -----------------------------------------------------------------------

        private class FakeSaveLoader : SaveLoader
        {
            public event Action OnClear;
            public bool ThrowOnLoad;

            // Store values as JSON strings so Load<T> round-trips any type correctly,
            // including JToken, int, and complex objects.
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public bool Has(string id) => _store.ContainsKey(id);

            public T Load<T>(string id, T defaultValue = default)
            {
                if (ThrowOnLoad)
                    throw new InvalidOperationException("Simulated load failure");

                if (_store.TryGetValue(id, out string json))
                {
                    T result = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
                    return result != null ? result : defaultValue;
                }
                return defaultValue;
            }

            public void Save<T>(string id, T value) =>
                _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);

            public void Delete(string id) => _store.Remove(id);
            public void Clear() { _store.Clear(); OnClear?.Invoke(); }
        }

        private class StubDataSaveLoader : IDataSaveLoader
        {
            public string DataId { get; set; }
            public bool LoadDefaultCalled;
            public bool LoadCalled;
            public JToken LastLoadedToken;
            public JToken SerializedValue = JToken.FromObject(new { value = 42 });
            public int GetSerializedDataCallCount;

            public StubDataSaveLoader(string id) => DataId = id;

            public void Load(JToken value) { LoadCalled = true; LastLoadedToken = value; }
            public void LoadDefault() => LoadDefaultCalled = true;
            public JToken GetSerializedData() { GetSerializedDataCallCount++; return SerializedValue; }

            public void ResetTracking()
            {
                LoadCalled = false;
                LoadDefaultCalled = false;
                LastLoadedToken = null;
                GetSerializedDataCallCount = 0;
            }
        }

        /// <summary>
        /// A DataSaveLoader stub that explicitly opts into dirty tracking.
        /// IsDirty starts true; MarkClean() sets it false; set IsDirtyManually to flip it back.
        /// </summary>
        private class DirtyTrackingStub : IDataSaveLoader
        {
            public string DataId { get; }
            public bool _isDirty = true;
            public int MarkCleanCallCount;
            public int GetSerializedDataCallCount;

            bool IDataSaveLoader.IsDirty => _isDirty;
            void IDataSaveLoader.MarkClean() { MarkCleanCallCount++; _isDirty = false; }

            public DirtyTrackingStub(string id) => DataId = id;

            public void Load(JToken value) { }
            public void LoadDefault() { }
            public JToken GetSerializedData() { GetSerializedDataCallCount++; return JToken.FromObject(42); }
        }

        private class StubGameDataMigrator : IGameDataMigrator
        {
            public int Version { get; set; }
            public bool MigrateCalled;

            public StubGameDataMigrator(int version) => Version = version;

            public void Migrate(Dictionary<string, JToken> data)
            {
                MigrateCalled = true;
                data["__migrated__"] = JToken.FromObject(Version);
            }
        }

        // -----------------------------------------------------------------------
        // Infrastructure
        // -----------------------------------------------------------------------

        private static JsonSerializer BuildSerializer()
        {
            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            r.Setup(x => x.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(r.Object);
            return serializer;
        }

        private static GameDataWriter BuildWriter(SaveLoader saveLoader, string gameDataId, int currentVersion)
        {
            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<SaveLoader>()).Returns(saveLoader);
            r.Setup(x => x.Resolve<GameDataWriter.Arguments>()).Returns(
                new GameDataWriter.Arguments
                {
                    GameDataId = gameDataId,
                    CurrentVersion = currentVersion,
                });

            GameDataWriter writer = new GameDataWriter();
            ((Injectable)writer).Inject(r.Object);
            return writer;
        }

        private static GameDataPersistence BuildPersistence(
            FakeSaveLoader saveLoader,
            JsonSerializer serializer,
            string gameDataId,
            int minVersion,
            int currentVersion,
            IReadOnlyList<IDataSaveLoader> dataLoaders,
            IReadOnlyList<IGameDataMigrator> migrators = null)
        {
            GameDataWriter writer = BuildWriter(saveLoader, gameDataId, currentVersion);

            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<SaveLoader>()).Returns(saveLoader);
            r.Setup(x => x.Resolve<JsonSerializer>()).Returns(serializer);
            r.Setup(x => x.Resolve<GameDataWriter>()).Returns(writer);
            r.Setup(x => x.Resolve<GameDataPersistence.Arguments>()).Returns(
                new GameDataPersistence.Arguments
                {
                    GameDataId = gameDataId,
                    MinSupportedVersion = minVersion,
                    CurrentVersion = currentVersion,
                    SaveLoaders = dataLoaders,
                    Migrators = migrators
                });

            GameDataPersistence persistence = new GameDataPersistence();
            ((Injectable)persistence).Inject(r.Object);
            ((Initializable)persistence).Initialize();
            return persistence;
        }

        // -----------------------------------------------------------------------
        // Load — no saved data => LoadDefault called on all DataSaveLoaders
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_no_data_1")]
        [TestCase("gdp_no_data_2")]
        [Description("Load() when no saved data exists => LoadDefault() called on all DataSaveLoaders, LoadingFailed is false")]
        public void Load_NoSavedData_LoadDefaultCalledOnAllLoaders(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loaderA = new StubDataSaveLoader("loaderA");
            StubDataSaveLoader loaderB = new StubDataSaveLoader("loaderB");

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loaderA, loaderB });

            persistence.Load();

            Assert.That(persistence.LoadingFailed.Value, Is.False);
            Assert.That(loaderA.LoadDefaultCalled, Is.True);
            Assert.That(loaderB.LoadDefaultCalled, Is.True);
        }

        // -----------------------------------------------------------------------
        // Load — saved data at current version => correct JToken dispatched
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_current_ver_1")]
        [TestCase("gdp_current_ver_2")]
        [Description("Load() with saved data at current version => Load(JToken) dispatched, LoadDefault() not called")]
        public void Load_SavedDataAtCurrentVersion_CorrectTokenDispatchedToLoaders(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loader = new StubDataSaveLoader("myData")
            {
                SerializedValue = JToken.FromObject(new { score = 99 })
            };

            // Save so there is data in the store at version 1.
            GameDataPersistence writer = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loader });
            writer.Load();
            writer.Save();

            loader.ResetTracking();

            GameDataPersistence reader = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loader });
            reader.Load();

            Assert.That(reader.LoadingFailed.Value, Is.False);
            Assert.That(loader.LoadCalled, Is.True);
            Assert.That(loader.LoadDefaultCalled, Is.False);
        }

        // -----------------------------------------------------------------------
        // Load — stored version below MinSupportedVersion => DataWasReset, LoadDefault
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_below_min_1")]
        [TestCase("gdp_below_min_2")]
        [Description("Load() when stored version is below MinSupportedVersion => DataWasReset becomes true and LoadDefault called")]
        public void Load_StoredVersionBelowMinSupported_DataWasResetAndLoadDefaultCalled(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loader = new StubDataSaveLoader("resetData");

            // Save at version 0.
            GameDataPersistence writer = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 0,
                new IDataSaveLoader[] { loader });
            writer.Load();
            writer.Save();

            loader.ResetTracking();

            // Reload with minSupportedVersion = 1 (stored v0 is too old).
            GameDataPersistence reader = BuildPersistence(
                saveLoader, serializer, gameDataId, minVersion: 1, currentVersion: 1,
                new IDataSaveLoader[] { loader });
            reader.Load();

            Assert.That(reader.DataWasReset.Value, Is.True);
            Assert.That(loader.LoadDefaultCalled, Is.True);
        }

        // -----------------------------------------------------------------------
        // Load — stale version above min => migrators applied before dispatch
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_migrate_1")]
        [TestCase("gdp_migrate_2")]
        [Description("Load() with stale-but-supported version => GameDataMigrators applied before DataSaveLoader.Load()")]
        public void Load_StaleVersion_MigratorsAppliedBeforeDispatch(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loader = new StubDataSaveLoader("migratedData");

            // Save at version 0.
            GameDataPersistence writer = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 0,
                new IDataSaveLoader[] { loader });
            writer.Load();
            writer.Save();

            loader.ResetTracking();

            StubGameDataMigrator migrator = new StubGameDataMigrator(version: 1);

            GameDataPersistence reader = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loader },
                new IGameDataMigrator[] { migrator });
            reader.Load();

            Assert.That(reader.LoadingFailed.Value, Is.False);
            Assert.That(migrator.MigrateCalled, Is.True);
        }

        // -----------------------------------------------------------------------
        // Load — two migrators applied in ascending version order
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_order_1")]
        [TestCase("gdp_order_2")]
        [Description("Load() with two migrators registered in reverse => both called in ascending version order")]
        public void Load_TwoMigrators_AppliedInAscendingVersionOrder(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loader = new StubDataSaveLoader("orderedData");

            // Save at version 0.
            GameDataPersistence writer = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 0,
                new IDataSaveLoader[] { loader });
            writer.Load();
            writer.Save();

            loader.ResetTracking();

            // Register in reverse to prove sorting.
            StubGameDataMigrator migratorV2 = new StubGameDataMigrator(version: 2);
            StubGameDataMigrator migratorV1 = new StubGameDataMigrator(version: 1);

            GameDataPersistence reader = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 2,
                new IDataSaveLoader[] { loader },
                new IGameDataMigrator[] { migratorV2, migratorV1 });
            reader.Load();

            Assert.That(reader.LoadingFailed.Value, Is.False);
            Assert.That(migratorV1.MigrateCalled, Is.True);
            Assert.That(migratorV2.MigrateCalled, Is.True);
        }

        // -----------------------------------------------------------------------
        // Load — SaveLoader.Load() throws => LoadingFailed becomes true
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_load_throws_1")]
        [TestCase("gdp_load_throws_2")]
        [Description("Load() when SaveLoader.Load() throws => LoadingFailed becomes true")]
        public void Load_SaveLoaderThrows_LoadingFailedBecomesTrue(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader { ThrowOnLoad = true };
            StubDataSaveLoader loader = new StubDataSaveLoader("anyData");

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loader });

            LogAssert.Expect(LogType.Error, new Regex("Failed to load game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            persistence.Load();

            Assert.That(persistence.LoadingFailed.Value, Is.True);
        }

        // -----------------------------------------------------------------------
        // Load — duplicate DataId => LoadingFailed becomes true
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_dup_id_1")]
        [TestCase("gdp_dup_id_2")]
        [Description("Load() when two DataSaveLoaders share the same DataId => LoadingFailed becomes true")]
        public void Load_DuplicateDataId_LoadingFailedBecomesTrue(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[]
                {
                    new StubDataSaveLoader("sameId"),
                    new StubDataSaveLoader("sameId")
                });

            LogAssert.Expect(LogType.Error, new Regex("Failed to load game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            persistence.Load();

            Assert.That(persistence.LoadingFailed.Value, Is.True);
        }

        // -----------------------------------------------------------------------
        // Save — after successful Load => version key and loader key present in store
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_save_1")]
        [TestCase("gdp_save_2")]
        [Description("Save() after successful Load() => version key and loader data key are present in store")]
        public void Save_AfterSuccessfulLoad_VersionKeyAndLoaderKeyPresentInStore(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { new StubDataSaveLoader("saveData") });
            persistence.Load();

            persistence.Save();

            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
            Assert.That(saveLoader.Has("saveData"), Is.True);
        }

        // -----------------------------------------------------------------------
        // Save — when LoadingFailed => SaveLoader.Save() never called
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_save_noop_1")]
        [TestCase("gdp_save_noop_2")]
        [Description("Save() when LoadingFailed is true => version key stays absent in store")]
        public void Save_WhenLoadingFailed_SaveNeverCalled(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader { ThrowOnLoad = true };

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { new StubDataSaveLoader("failData") });
            LogAssert.Expect(LogType.Error, new Regex("Failed to load game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            persistence.Load();  // sets LoadingFailed = true

            saveLoader.ThrowOnLoad = false;
            LogAssert.Expect(LogType.Error, new Regex("Save was aborted"));
            persistence.Save();

            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.False);
        }

        // -----------------------------------------------------------------------
        // Save — dirty loader is serialized; clean loader is skipped
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_dirty_1")]
        [TestCase("gdp_dirty_2")]
        [Description("Save() with a clean loader => GetSerializedData() not called for the clean loader")]
        public void Save_CleanLoader_GetSerializedDataNotCalled(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            DirtyTrackingStub cleanLoader = new DirtyTrackingStub("cleanData") { _isDirty = false };

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { cleanLoader });
            persistence.Load();

            persistence.Save();

            Assert.That(cleanLoader.GetSerializedDataCallCount, Is.Zero);
        }

        [Test]
        [TestCase("gdp_dirty_write_1")]
        [TestCase("gdp_dirty_write_2")]
        [Description("Save() with a dirty loader => GetSerializedData() called and key present in store")]
        public void Save_DirtyLoader_GetSerializedDataCalledAndKeyWritten(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            DirtyTrackingStub dirtyLoader = new DirtyTrackingStub("dirtyData") { _isDirty = true };

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { dirtyLoader });
            persistence.Load();

            persistence.Save();

            Assert.That(dirtyLoader.GetSerializedDataCallCount, Is.EqualTo(1));
            Assert.That(saveLoader.Has("dirtyData"), Is.True);
        }

        // -----------------------------------------------------------------------
        // Save — all loaders marked clean after a successful save
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_markclean_1")]
        [TestCase("gdp_markclean_2")]
        [Description("Save() succeeds => MarkClean() called on every loader exactly once")]
        public void Save_Success_AllLoadersMarkedClean(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            DirtyTrackingStub loaderA = new DirtyTrackingStub("dataA");
            DirtyTrackingStub loaderB = new DirtyTrackingStub("dataB");

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { loaderA, loaderB });
            persistence.Load();

            persistence.Save();

            Assert.That(loaderA.MarkCleanCallCount, Is.EqualTo(1));
            Assert.That(loaderB.MarkCleanCallCount, Is.EqualTo(1));
        }

        // -----------------------------------------------------------------------
        // Save — serialization failure => nothing written, loaders stay dirty
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_savefail_1")]
        [TestCase("gdp_savefail_2")]
        [Description("Save() when GetSerializedData() throws => no data written and loaders remain dirty")]
        public void Save_SerializationThrows_NothingWrittenAndLoaderStaysDirty(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();

            // A loader whose GetSerializedData() throws.
            ThrowingStub throwingLoader = new ThrowingStub("badData");

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { throwingLoader });
            persistence.Load();

            LogAssert.Expect(LogType.Error, new Regex("Failed to save game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            persistence.Save();

            // Nothing should have been written.
            Assert.That(saveLoader.Has("badData"), Is.False);
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.False);
        }

        private class ThrowingStub : IDataSaveLoader
        {
            public string DataId { get; }
            public ThrowingStub(string id) => DataId = id;
            public void Load(JToken value) { }
            public void LoadDefault() { }
            public JToken GetSerializedData() => throw new InvalidOperationException("Simulated serialization failure");
        }

        // -----------------------------------------------------------------------
        // OverrideSave — happy path => version key present in store
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_override_1")]
        [TestCase("gdp_override_2")]
        [Description("OverrideSave() happy path => version key and custom data key are present in store")]
        public void OverrideSave_HappyPath_VersionKeyAndDataKeyPresentInStore(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { new StubDataSaveLoader("overrideData") });
            persistence.Load();

            persistence.OverrideSave(
                new Dictionary<string, JToken> { { "custom", JToken.FromObject(new { x = 7 }) } },
                version: 5);

            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
            Assert.That(saveLoader.Has("custom"), Is.True);
        }

        // -----------------------------------------------------------------------
        // OverrideSave — when LoadingFailed => SaveLoader.Save() never called
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_override_noop_1")]
        [TestCase("gdp_override_noop_2")]
        [Description("OverrideSave() when LoadingFailed is true => version key stays absent in store")]
        public void OverrideSave_WhenLoadingFailed_SaveNeverCalled(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader { ThrowOnLoad = true };

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 1,
                new IDataSaveLoader[] { new StubDataSaveLoader("failOverride") });
            LogAssert.Expect(LogType.Error, new Regex("Failed to load game data"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));
            persistence.Load();

            saveLoader.ThrowOnLoad = false;
            LogAssert.Expect(LogType.Error, new Regex("Override save was aborted"));
            persistence.OverrideSave(
                new Dictionary<string, JToken> { { "custom", JToken.FromObject(42) } },
                version: 1);

            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.False);
        }

        // -----------------------------------------------------------------------
        // Initialize — observable flags are false after initialization
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_init_1")]
        [TestCase("gdp_init_2")]
        [Description("Initialize() => LoadingFailed and DataWasReset are both false")]
        public void Initialize_ObservableFlagsAreFalse(string gameDataId)
        {
            GameDataPersistence persistence = BuildPersistence(
                new FakeSaveLoader(), BuildSerializer(), gameDataId, 0, 1,
                new IDataSaveLoader[] { });

            Assert.That(persistence.LoadingFailed.Value, Is.False);
            Assert.That(persistence.DataWasReset.Value, Is.False);
        }

        // -----------------------------------------------------------------------
        // Legacy blob migration — v1.5 single-blob format migrated to per-key on Load
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("gdp_legacy_1")]
        [TestCase("gdp_legacy_2")]
        [Description("Load() when legacy single-blob GameData exists => entries migrated to per-key, old blob key deleted, loaders receive their data")]
        public void Load_LegacyBlobFormat_MigratedToPerKeyAndLoadersReceiveData(string gameDataId)
        {
            JsonSerializer serializer = BuildSerializer();
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            StubDataSaveLoader loader = new StubDataSaveLoader("legacyEntry")
            {
                SerializedValue = JToken.FromObject(new { score = 77 })
            };

            // Simulate a legacy single-blob save (as produced by v1.5 and earlier).
            GameData legacyData = new GameData
            {
                StructureVersion = 1,
                Version = 0,
                Entries = new[]
                {
                    new GameDataEntry { Id = "legacyEntry", Payload = JToken.FromObject(new { score = 77 }) }
                }
            };
            saveLoader.Save(gameDataId, legacyData);

            GameDataPersistence persistence = BuildPersistence(
                saveLoader, serializer, gameDataId, 0, 0,
                new IDataSaveLoader[] { loader });
            persistence.Load();

            Assert.That(persistence.LoadingFailed.Value, Is.False);
            // Old blob key must be gone.
            Assert.That(saveLoader.Has(gameDataId), Is.False);
            // New per-key format must be present.
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
            Assert.That(saveLoader.Has("legacyEntry"), Is.True);
            // Loader must have received its data.
            Assert.That(loader.LoadCalled, Is.True);
        }
    }
}
