using System;
using System.Diagnostics;
using System.IO;
using Calluna.DI;
using Calluna.Persistence;
using Moq;
using NUnit.Framework;
using UnityEngine;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Performance benchmarks for all three <see cref="SaveLoader"/> backends.
    /// Each test runs a warm-up block to trigger JIT compilation, then a timed measurement
    /// block, and asserts Pass with a formatted comparison table so results appear in
    /// the Test Runner output.
    ///
    /// Run these tests in isolation (not alongside the full suite) for stable numbers.
    /// Filter by Category "Performance" in the Test Runner to select only these tests.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public class SaveLoaderPerformanceTests
    {
        // Number of operations per measured block.
        private const int Iterations = 200;

        // Number of DataSaveLoader-equivalent keys written per simulated game save.
        private const int KeysPerSave = 5;

        // Number of simulated game-save rounds in the realistic-pattern benchmark.
        private const int SaveRounds = 100;

        private PlayerPrefsSaveLoader _playerPrefs;
        private PersistentDataPathSaveLoader _persistentDataPath;
        private string _persistentDataPathFile;
        private SqliteSaveLoader _sqlite;
        private string _sqliteFile;
        private SqliteSaveLoader _sqliteSyncOff;
        private string _sqliteSyncOffFile;

        private class Payload
        {
            public string Name  { get; set; }
            public int    Score { get; set; }
            public float  Progress { get; set; }
        }

        // -----------------------------------------------------------------------
        // Setup / Teardown
        // -----------------------------------------------------------------------

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteAll();
            _playerPrefs = BuildPlayerPrefsLoader();

            string guid = Guid.NewGuid().ToString("N");

            _persistentDataPathFile = $"PerfTest_{guid}.json";
            _persistentDataPath = BuildPersistentDataPathLoader(_persistentDataPathFile);

            _sqliteFile = $"PerfTest_{guid}.db";
            _sqlite = BuildSqliteLoader(_sqliteFile);

            _sqliteSyncOffFile = $"PerfTest_{guid}_syncoff.db";
            _sqliteSyncOff = BuildSqliteLoader(_sqliteSyncOffFile, synchronousOff: true);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteAll();

            try { ((Cleanable)_persistentDataPath).Clean(); } catch { /* ignore */ }
            string pdpPath = Path.Combine(Application.persistentDataPath, _persistentDataPathFile);
            if (File.Exists(pdpPath)) File.Delete(pdpPath);

            try { ((Cleanable)_sqlite).Clean(); } catch { /* ignore */ }
            string sqlitePath = Path.Combine(Application.persistentDataPath, _sqliteFile);
            DeleteIfExists(sqlitePath);
            DeleteIfExists(sqlitePath + "-wal");
            DeleteIfExists(sqlitePath + "-shm");

            try { ((Cleanable)_sqliteSyncOff).Clean(); } catch { /* ignore */ }
            string sqliteSyncOffPath = Path.Combine(Application.persistentDataPath, _sqliteSyncOffFile);
            DeleteIfExists(sqliteSyncOffPath);
            DeleteIfExists(sqliteSyncOffPath + "-wal");
            DeleteIfExists(sqliteSyncOffPath + "-shm");
        }

        // -----------------------------------------------------------------------
        // Benchmarks
        // -----------------------------------------------------------------------

        /// <summary>
        /// Writes <see cref="Iterations"/> distinct keys, one at a time, with a complex payload.
        /// Measures raw write throughput across different keys.
        /// </summary>
        [Test]
        public void Benchmark_SequentialWrites_DifferentKeys()
        {
            Payload payload = new Payload { Name = "Hero", Score = 9001, Progress = 0.75f };

            void WriteAll(SaveLoader loader)
            {
                for (int i = 0; i < Iterations; i++)
                    loader.Save($"key_{i}", payload);
            }

            // Warm up (triggers JIT and file/DB initialisation).
            WriteAll(_playerPrefs);
            WriteAll(_persistentDataPath);
            WriteAll(_sqlite);
            WriteAll(_sqliteSyncOff);

            long ppMs      = MeasureMs(() => WriteAll(_playerPrefs));
            long pdpMs     = MeasureMs(() => WriteAll(_persistentDataPath));
            long sqlMs     = MeasureMs(() => WriteAll(_sqlite));
            long sqlSyncMs = MeasureMs(() => WriteAll(_sqliteSyncOff));

            Assert.Pass(FormatTable(
                $"Sequential writes — {Iterations} distinct keys, complex payload",
                Iterations, ppMs, pdpMs, sqlMs, sqlSyncMs));
        }

        /// <summary>
        /// Writes the same single key <see cref="Iterations"/> times (overwrite pattern).
        /// Relevant for values that change frequently, such as a score or position.
        /// </summary>
        [Test]
        public void Benchmark_RepeatedOverwrite_SingleKey()
        {
            const string key = "frequently_updated_key";
            Payload payload = new Payload { Name = "Hero", Score = 0, Progress = 0f };

            void OverwriteAll(SaveLoader loader)
            {
                for (int i = 0; i < Iterations; i++)
                {
                    payload.Score = i;
                    loader.Save(key, payload);
                }
            }

            OverwriteAll(_playerPrefs);
            OverwriteAll(_persistentDataPath);
            OverwriteAll(_sqlite);
            OverwriteAll(_sqliteSyncOff);

            long ppMs      = MeasureMs(() => OverwriteAll(_playerPrefs));
            long pdpMs     = MeasureMs(() => OverwriteAll(_persistentDataPath));
            long sqlMs     = MeasureMs(() => OverwriteAll(_sqlite));
            long sqlSyncMs = MeasureMs(() => OverwriteAll(_sqliteSyncOff));

            Assert.Pass(FormatTable(
                $"Repeated overwrite — {Iterations} writes to the same key",
                Iterations, ppMs, pdpMs, sqlMs, sqlSyncMs));
        }

        /// <summary>
        /// Reads <see cref="Iterations"/> existing keys.
        /// Measures read throughput; all keys are pre-populated in the warm-up block.
        /// </summary>
        [Test]
        public void Benchmark_SequentialReads_ExistingKeys()
        {
            Payload payload = new Payload { Name = "Hero", Score = 9001, Progress = 0.75f };

            // Pre-populate (also serves as warm-up for writing).
            for (int i = 0; i < Iterations; i++)
            {
                _playerPrefs.Save($"key_{i}", payload);
                _persistentDataPath.Save($"key_{i}", payload);
                _sqlite.Save($"key_{i}", payload);
                _sqliteSyncOff.Save($"key_{i}", payload);
            }

            void ReadAll(SaveLoader loader)
            {
                for (int i = 0; i < Iterations; i++)
                    loader.Load<Payload>($"key_{i}");
            }

            // Warm up reads.
            ReadAll(_playerPrefs);
            ReadAll(_persistentDataPath);
            ReadAll(_sqlite);
            ReadAll(_sqliteSyncOff);

            long ppMs      = MeasureMs(() => ReadAll(_playerPrefs));
            long pdpMs     = MeasureMs(() => ReadAll(_persistentDataPath));
            long sqlMs     = MeasureMs(() => ReadAll(_sqlite));
            long sqlSyncMs = MeasureMs(() => ReadAll(_sqliteSyncOff));

            Assert.Pass(FormatTable(
                $"Sequential reads — {Iterations} existing keys, complex payload",
                Iterations, ppMs, pdpMs, sqlMs, sqlSyncMs));
        }

        /// <summary>
        /// Simulates a realistic GameData save: <see cref="KeysPerSave"/> keys written together
        /// per save round, repeated <see cref="SaveRounds"/> times. Mirrors how
        /// <see cref="GameDataPersistence.Save"/> works — if the loader supports
        /// <see cref="IBatchableSaveLoader"/>, all writes per round are wrapped in one transaction.
        /// </summary>
        [Test]
        public void Benchmark_GameSavePattern()
        {
            Payload[] payloads = new Payload[KeysPerSave];
            for (int k = 0; k < KeysPerSave; k++)
                payloads[k] = new Payload { Name = $"domain_{k}", Score = k * 100, Progress = k * 0.1f };

            void SimulateSaveRounds(SaveLoader loader)
            {
                IBatchableSaveLoader batchable = loader as IBatchableSaveLoader;
                for (int round = 0; round < SaveRounds; round++)
                {
                    batchable?.BeginBatch();
                    for (int k = 0; k < KeysPerSave; k++)
                    {
                        payloads[k].Score = round * KeysPerSave + k;
                        loader.Save($"domain_{k}", payloads[k]);
                    }
                    batchable?.CommitBatch();
                }
            }

            SimulateSaveRounds(_playerPrefs);
            SimulateSaveRounds(_persistentDataPath);
            SimulateSaveRounds(_sqlite);
            SimulateSaveRounds(_sqliteSyncOff);

            long ppMs      = MeasureMs(() => SimulateSaveRounds(_playerPrefs));
            long pdpMs     = MeasureMs(() => SimulateSaveRounds(_persistentDataPath));
            long sqlMs     = MeasureMs(() => SimulateSaveRounds(_sqlite));
            long sqlSyncMs = MeasureMs(() => SimulateSaveRounds(_sqliteSyncOff));

            int totalWrites = SaveRounds * KeysPerSave;
            Assert.Pass(FormatTable(
                $"GameData save pattern — {SaveRounds} rounds × {KeysPerSave} keys = {totalWrites} writes (SQLite: 1 tx/round)",
                totalWrites, ppMs, pdpMs, sqlMs, sqlSyncMs));
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static long MeasureMs(Action action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private static string FormatTable(string scenario, int operations,
            long playerPrefsMs, long persistentMs, long sqliteMs,
            long? sqliteSyncOffMs = null)
        {
            long fastest = Math.Min(playerPrefsMs, Math.Min(persistentMs,
                Math.Min(sqliteMs, sqliteSyncOffMs ?? long.MaxValue)));
            string Rel(long ms) => fastest == 0 ? "—" : $"{(double)ms / fastest:F1}×";

            string table = $"\n\n{scenario}\n" +
                   $"  {"Backend",-26} {"Total (ms)",10}  {"Relative",10}  {"ms/op",8}\n" +
                   $"  {new string('-', 60)}\n" +
                   $"  {"PlayerPrefs",-26} {playerPrefsMs,10}  {Rel(playerPrefsMs),10}  {(double)playerPrefsMs / operations,8:F3}\n" +
                   $"  {"PersistentDataPath",-26} {persistentMs,10}  {Rel(persistentMs),10}  {(double)persistentMs / operations,8:F3}\n" +
                   $"  {"SQLite (WAL)",-26} {sqliteMs,10}  {Rel(sqliteMs),10}  {(double)sqliteMs / operations,8:F3}\n";

            if (sqliteSyncOffMs.HasValue)
                table += $"  {"SQLite (WAL + sync=OFF)",-26} {sqliteSyncOffMs.Value,10}  {Rel(sqliteSyncOffMs.Value),10}  {(double)sqliteSyncOffMs.Value / operations,8:F3}\n";

            return table;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // -----------------------------------------------------------------------
        // Loader factories (mirrors existing test helpers)
        // -----------------------------------------------------------------------

        private static PlayerPrefsSaveLoader BuildPlayerPrefsLoader()
        {
            JsonSerializer serializer = BuildSerializer();
            Mock<Resolver> resolver = new Mock<Resolver>();
            resolver.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);

            PlayerPrefsSaveLoader loader = new PlayerPrefsSaveLoader();
            ((Injectable)loader).Inject(resolver.Object);
            return loader;
        }

        private static PersistentDataPathSaveLoader BuildPersistentDataPathLoader(string fileName)
        {
            JsonSerializer serializer = BuildSerializer();
            TextFileReadWriter readWriter = new TextFileReadWriter();

            Mock<Resolver> resolver = new Mock<Resolver>();
            resolver.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);
            resolver.Setup(r => r.Resolve<TextFileReadWriter>()).Returns(readWriter);
            resolver.Setup(r => r.Resolve<PersistentDataPathSaveLoader.Arguments>())
                .Returns(new PersistentDataPathSaveLoader.Arguments { FileName = fileName });

            PersistentDataPathSaveLoader loader = new PersistentDataPathSaveLoader();
            ((Injectable)loader).Inject(resolver.Object);
            return loader;
        }

        private static SqliteSaveLoader BuildSqliteLoader(string fileName, bool synchronousOff = false)
        {
            JsonSerializer serializer = BuildSerializer();

            Mock<Resolver> resolver = new Mock<Resolver>();
            resolver.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);
            resolver.Setup(r => r.Resolve<SqliteSaveLoader.Arguments>())
                .Returns(new SqliteSaveLoader.Arguments { FileName = fileName, SynchronousOff = synchronousOff });

            SqliteSaveLoader loader = new SqliteSaveLoader();
            ((Injectable)loader).Inject(resolver.Object);
            return loader;
        }

        private static JsonSerializer BuildSerializer()
        {
            Mock<Resolver> resolver = new Mock<Resolver>();
            resolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            resolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(resolver.Object);
            return serializer;
        }
    }
}
