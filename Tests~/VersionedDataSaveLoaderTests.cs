using System;
using System.Collections.Generic;
using Calluna.DI;
using Calluna.Persistence;
using Moq;
using NUnit.Framework;
using UnityEngine;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Tests for <see cref="VersionedDataSaveLoader"/> and <see cref="VersionedDataMigrator{T}"/>.
    ///
    /// VersionedSaveData is internal, so we cannot mock SaveLoader.Load to return it
    /// directly. Instead we use a real PlayerPrefsSaveLoader for integration: Save then
    /// Load through the same VersionedDataSaveLoader instance exercises the full round-
    /// trip and allows us to verify migration logic.
    /// </summary>
    [TestFixture]
    public class VersionedDataSaveLoaderTests
    {
        // -----------------------------------------------------------------------
        // Shared helpers
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

        private static PlayerPrefsSaveLoader BuildPlayerPrefsSaveLoader(JsonSerializer serializer)
        {
            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<JsonSerializer>()).Returns(serializer);

            PlayerPrefsSaveLoader saveLoader = new PlayerPrefsSaveLoader();
            ((Injectable)saveLoader).Inject(r.Object);
            return saveLoader;
        }

        private static VersionedDataSaveLoader BuildVersionedLoader(
            SaveLoader saveLoader,
            JsonSerializer serializer,
            IEnumerable<VersionedDataMigrator> migrators = null)
        {
            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<SaveLoader>()).Returns(saveLoader);
            r.Setup(x => x.Resolve<JsonSerializer>()).Returns(serializer);
            r.Setup(x => x.ResolveOptional<IEnumerable<VersionedDataMigrator>>()).Returns(migrators);

            VersionedDataSaveLoader loader = new VersionedDataSaveLoader();
            ((Injectable)loader).Inject(r.Object);
            return loader;
        }

        // -----------------------------------------------------------------------
        // Concrete migration step helpers
        // -----------------------------------------------------------------------

        // v0 data shape used in migration tests.
        private class DataV0 { public int X { get; set; } }
        // v1 data shape — adds a string field derived from X.
        private class DataV1 { public int X { get; set; } public string Label { get; set; } }
        // v2 data shape — renames X to Value.
        private class DataV2 { public string Label { get; set; } public int Value { get; set; } }

        private class StepV0ToV1 : VersionedDataMigrationStep
        {
            private readonly JsonSerializer _serializer;
            public StepV0ToV1(JsonSerializer serializer) => _serializer = serializer;
            public override int TargetVersion => 1;
            public override string Migrate(string data)
            {
                DataV0 from = _serializer.Deserialize<DataV0>(data);
                DataV1 to = new DataV1 { X = from.X, Label = $"x={from.X}" };
                return _serializer.Serialize(to);
            }
        }

        private class StepV1ToV2 : VersionedDataMigrationStep
        {
            private readonly JsonSerializer _serializer;
            public StepV1ToV2(JsonSerializer serializer) => _serializer = serializer;
            public override int TargetVersion => 2;
            public override string Migrate(string data)
            {
                DataV1 from = _serializer.Deserialize<DataV1>(data);
                DataV2 to = new DataV2 { Label = from.Label, Value = from.X * 10 };
                return _serializer.Serialize(to);
            }
        }

        // -----------------------------------------------------------------------
        // SetUp / TearDown
        // -----------------------------------------------------------------------

        [SetUp]
        public void SetUp() => PlayerPrefs.DeleteAll();

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteAll();

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — null / missing value
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("missing_key_1")]
        [TestCase("missing_key_2")]
        [Description("Load<T>() when SaveLoader returns null (key absent) => returns default(T)?")]
        public void Load_MissingKey_ReturnsDefault(string key)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);
            VersionedDataMigrator<DataV1> migrator = new VersionedDataMigrator<DataV1>(
                new[] { new StepV0ToV1(serializer) });
            VersionedDataSaveLoader loader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            DataV1 result = loader.Load<DataV1>(key);

            Assert.That(result, Is.Null);
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — no migrator registered
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("no_migrator_key_1")]
        [TestCase("no_migrator_key_2")]
        [Description("Load<T>() when no VersionedDataMigrator is registered for T => throws ArgumentException?")]
        public void Load_NoMigrator_ThrowsArgumentException(string key)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);
            // Save something so there IS data to load (otherwise null returns before the migrator check).
            VersionedDataMigrator<DataV1> migrator = new VersionedDataMigrator<DataV1>(
                new[] { new StepV0ToV1(serializer) });
            VersionedDataSaveLoader writerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });
            writerLoader.Save(key, new DataV1 { X = 1, Label = "a" }, 1);

            // Reader has NO migrators at all.
            VersionedDataSaveLoader readerLoader = BuildVersionedLoader(saveLoader, serializer);

            Assert.Throws<ArgumentException>(() => readerLoader.Load<DataV1>(key));
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — current version, no migration
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("current_ver_1", 10, "current")]
        [TestCase("current_ver_2", -5, "label")]
        [Description("Load<T>() when saved version equals current version => no migration step applied, correct value returned?")]
        public void Load_AtCurrentVersion_ReturnsSavedValue(string key, int xVal, string label)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);
            VersionedDataMigrator<DataV1> migrator = new VersionedDataMigrator<DataV1>(
                new[] { new StepV0ToV1(serializer) });
            VersionedDataSaveLoader loader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            DataV1 original = new DataV1 { X = xVal, Label = label };
            loader.Save(key, original, version: 1);

            DataV1 result = loader.Load<DataV1>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.X, Is.EqualTo(xVal));
            Assert.That(result.Label, Is.EqualTo(label));
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — stale by one step
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("stale_one_1", 3)]
        [TestCase("stale_one_2", 7)]
        [Description("Load<T>() when saved version is stale by one step => single migration step applied and result returned?")]
        public void Load_StaleByOneStep_AppliesMigration(string key, int xVal)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);

            // Writer saves DataV0 at version 0 via the raw PlayerPrefs save loader so we
            // can control the stored version precisely. We serialise the VersionedSaveData
            // structure ourselves using reflection to avoid depending on the internal type.
            // Better: we save DataV0 via a loader that only knows about version 0, then
            // read back as DataV1 via a loader that has the step registered.

            // Save raw DataV0 at version 0 using a migrator with NO steps (current version = 0).
            VersionedDataMigrator<DataV0> v0Migrator = new VersionedDataMigrator<DataV0>();
            VersionedDataSaveLoader writerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { v0Migrator });
            writerLoader.Save(key, new DataV0 { X = xVal }, version: 0);

            // Read back as DataV1 using a loader with the v0→v1 step.
            VersionedDataMigrator<DataV1> v1Migrator = new VersionedDataMigrator<DataV1>(
                new[] { new StepV0ToV1(serializer) });
            VersionedDataSaveLoader readerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { v1Migrator });

            DataV1 result = readerLoader.Load<DataV1>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.X, Is.EqualTo(xVal));
            Assert.That(result.Label, Is.EqualTo($"x={xVal}"));
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — stale by two steps
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("stale_two_1", 4)]
        [TestCase("stale_two_2", 9)]
        [Description("Load<T>() when saved version is stale by two steps => both steps applied in order?")]
        public void Load_StaleByTwoSteps_AppliesBothMigrations(string key, int xVal)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);

            // Save DataV0 at version 0.
            VersionedDataMigrator<DataV0> v0Migrator = new VersionedDataMigrator<DataV0>();
            VersionedDataSaveLoader writerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { v0Migrator });
            writerLoader.Save(key, new DataV0 { X = xVal }, version: 0);

            // Read back as DataV2 after two steps: v0→v1→v2.
            VersionedDataMigrator<DataV2> v2Migrator = new VersionedDataMigrator<DataV2>(
                new VersionedDataMigrationStep[]
                {
                    new StepV0ToV1(serializer),
                    new StepV1ToV2(serializer)
                });
            VersionedDataSaveLoader readerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { v2Migrator });

            DataV2 result = readerLoader.Load<DataV2>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Label, Is.EqualTo($"x={xVal}"));
            Assert.That(result.Value, Is.EqualTo(xVal * 10));
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader round-trip
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("rt_1", 5, "hello")]
        [TestCase("rt_2", -2, "world")]
        [Description("Save<T>() then Load<T>() round-trip at the same version => original value returned unchanged?")]
        public void SaveLoad_RoundTrip_OriginalValueReturned(string key, int xVal, string label)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);
            VersionedDataMigrator<DataV1> migrator = new VersionedDataMigrator<DataV1>(
                new[] { new StepV0ToV1(serializer) });
            VersionedDataSaveLoader loader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            DataV1 original = new DataV1 { X = xVal, Label = label };
            loader.Save(key, original, version: 1);
            DataV1 result = loader.Load<DataV1>(key);

            Assert.That(result.X, Is.EqualTo(xVal));
            Assert.That(result.Label, Is.EqualTo(label));
        }

        // -----------------------------------------------------------------------
        // VersionedDataMigrator<T> — no steps
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("migrator_no_steps_1", 1)]
        [TestCase("migrator_no_steps_2", 99)]
        [Description("VersionedDataMigrator<T> constructor with no steps => Migrate with version 0 returns data unchanged?")]
        public void VersionedDataMigrator_NoSteps_ReturnsDataUnchanged(string key, int xVal)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);
            // No steps: current version is 0, so nothing to migrate.
            VersionedDataMigrator<DataV0> migrator = new VersionedDataMigrator<DataV0>();
            VersionedDataSaveLoader loader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            loader.Save(key, new DataV0 { X = xVal }, version: 0);
            DataV0 result = loader.Load<DataV0>(key);

            Assert.That(result.X, Is.EqualTo(xVal));
        }

        // -----------------------------------------------------------------------
        // VersionedDataMigrator<T> — tracks highest TargetVersion
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("highest_ver_1", 2)]
        [TestCase("highest_ver_2", 8)]
        [Description("VersionedDataMigrator<T> constructor with steps => tracks highest TargetVersion as current version?")]
        public void VersionedDataMigrator_WithSteps_CurrentVersionIsHighestTargetVersion(string key, int xVal)
        {
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);

            // Register two steps; highest target is v2. If current version is correctly v2,
            // saving at v2 and loading at v2 should apply zero migration steps.
            VersionedDataMigrator<DataV2> migrator = new VersionedDataMigrator<DataV2>(
                new VersionedDataMigrationStep[]
                {
                    new StepV0ToV1(serializer),
                    new StepV1ToV2(serializer)
                });
            VersionedDataSaveLoader loader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            DataV2 original = new DataV2 { Label = "test", Value = xVal };
            loader.Save(key, original, version: 2);
            DataV2 result = loader.Load<DataV2>(key);

            Assert.That(result.Value, Is.EqualTo(xVal));
            Assert.That(result.Label, Is.EqualTo("test"));
        }

        // -----------------------------------------------------------------------
        // VersionedDataMigrator — missing intermediate step
        // -----------------------------------------------------------------------

        [Test]
        [Description("VersionedDataMigrator<T> constructor with a gap in steps => throws InvalidOperationException at construction time?")]
        public void VersionedDataMigrator_MissingIntermediateStep_ThrowsInvalidOperationException()
        {
            JsonSerializer serializer = BuildSerializer();

            // Register only step v1→v2 (skipping v0→v1). The gap should be caught immediately
            // at construction, before any Load() is attempted.
            Assert.Throws<InvalidOperationException>(() =>
                new VersionedDataMigrator<DataV2>(
                    new VersionedDataMigrationStep[] { new StepV1ToV2(serializer) }));
        }

        // -----------------------------------------------------------------------
        // VersionedDataMigrator — duplicate TargetVersion silently overwrites
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("dup_ver_1", 5)]
        [TestCase("dup_ver_2", 8)]
        [Description("VersionedDataMigrator<T> constructor with duplicate TargetVersion steps => last step wins (silently overwrites)?")]
        public void VersionedDataMigrator_DuplicateTargetVersion_LastStepWins(string key, int xVal)
        {
            // Both steps claim TargetVersion == 1. The constructor does NOT throw —
            // the dictionary assignment silently overwrites. The second step (StepV0ToV1)
            // transforms X and adds a Label; the first (a no-op version that just copies)
            // would leave Label null. By asserting Label is set we confirm the later
            // registration (last in the array) is the one that runs.
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);

            // "NoOpStep" for v1: copies DataV0 → DataV1 but leaves Label null.
            VersionedDataMigrationStep noOpStep = new NoOpStepV0ToV1(serializer);
            // Real step for v1: produces Label = "x=<xVal>".
            VersionedDataMigrationStep realStep = new StepV0ToV1(serializer);

            // Constructor should not throw for duplicate TargetVersion.
            VersionedDataMigrator<DataV1> migrator = null;
            Assert.DoesNotThrow(() =>
                migrator = new VersionedDataMigrator<DataV1>(
                    new VersionedDataMigrationStep[] { noOpStep, realStep }));

            // Save DataV0 at version 0.
            VersionedDataMigrator<DataV0> v0Migrator = new VersionedDataMigrator<DataV0>();
            VersionedDataSaveLoader writerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { v0Migrator });
            writerLoader.Save(key, new DataV0 { X = xVal }, version: 0);

            // Load as DataV1 — the last-registered step for v1 will run.
            VersionedDataSaveLoader readerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { migrator });

            DataV1 result = readerLoader.Load<DataV1>(key);

            // The step that was applied last wins: the real step gives Label = "x={xVal}".
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Label, Is.EqualTo($"x={xVal}"));
        }

        // -----------------------------------------------------------------------
        // VersionedDataSaveLoader.Load<T> — stored version > current version
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("future_ver_1", 3, "future")]
        [TestCase("future_ver_2", 11, "ahead")]
        [Description("Load<T>() when stored version is greater than migrator current version => data returned unchanged (no crash, no mutation)?")]
        public void Load_StoredVersionAheadOfCurrent_ReturnsDataUnchanged(string key, int xVal, string label)
        {
            // Setup: write DataV2 at version 3 (one ahead of what the reader migrator knows).
            // The reader migrator has currentVersion=2 (highest TargetVersion=2).
            // Stored version 3 > currentVersion 2: the Migrate() loop
            //   "for (int i = version; i < _currentVersion; i++)" never executes
            //   because 3 < 2 is false → data is returned as-is without modification.
            JsonSerializer serializer = BuildSerializer();
            PlayerPrefsSaveLoader saveLoader = BuildPlayerPrefsSaveLoader(serializer);

            VersionedDataMigrator<DataV2> writerMigrator = new VersionedDataMigrator<DataV2>(
                new VersionedDataMigrationStep[]
                {
                    new StepV0ToV1(serializer),
                    new StepV1ToV2(serializer)
                });
            VersionedDataSaveLoader writerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { writerMigrator });

            // Save at version 3 — above any migrator's current version.
            DataV2 original = new DataV2 { Label = label, Value = xVal };
            writerLoader.Save(key, original, version: 3);

            // Reader uses a migrator that only knows up to version 2.
            VersionedDataMigrator<DataV2> readerMigrator = new VersionedDataMigrator<DataV2>(
                new VersionedDataMigrationStep[]
                {
                    new StepV0ToV1(serializer),
                    new StepV1ToV2(serializer)
                });
            VersionedDataSaveLoader readerLoader = BuildVersionedLoader(saveLoader, serializer,
                new VersionedDataMigrator[] { readerMigrator });

            DataV2 result = readerLoader.Load<DataV2>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Label, Is.EqualTo(label));
            Assert.That(result.Value, Is.EqualTo(xVal));
        }

        // -----------------------------------------------------------------------
        // Helper step: no-op v0→v1 (copies X, leaves Label null)
        // -----------------------------------------------------------------------

        private class NoOpStepV0ToV1 : VersionedDataMigrationStep
        {
            private readonly JsonSerializer _serializer;
            public NoOpStepV0ToV1(JsonSerializer serializer) => _serializer = serializer;
            public override int TargetVersion => 1;
            public override string Migrate(string data)
            {
                DataV0 from = _serializer.Deserialize<DataV0>(data);
                DataV1 to = new DataV1 { X = from.X, Label = null };
                return _serializer.Serialize(to);
            }
        }
    }
}
