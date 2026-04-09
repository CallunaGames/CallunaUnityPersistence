using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
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
    /// Direct unit tests for <see cref="GameDataWriter"/>.
    /// <see cref="GameDataPersistenceTests"/> covers <see cref="GameDataWriter"/> only
    /// indirectly; these tests assert its own behaviour in isolation.
    /// </summary>
    [TestFixture]
    public class GameDataWriterTests
    {
        // -----------------------------------------------------------------------
        // Test doubles
        // -----------------------------------------------------------------------

        private class FakeSaveLoader : SaveLoader
        {
            public event Action OnClear;
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public bool Has(string id) => _store.ContainsKey(id);
            public T Load<T>(string id, T defaultValue = default) => defaultValue;
            public void Save<T>(string id, T value) =>
                _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            public void Delete(string id) => _store.Remove(id);
            public void Clear() { _store.Clear(); OnClear?.Invoke(); }
        }

        private class FakeBatchableSaveLoader : SaveLoader, IBatchableSaveLoader
        {
            public event Action OnClear;
            public int BeginBatchCallCount;
            public int CommitBatchCallCount;
            public int RollbackBatchCallCount;

            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public bool Has(string id) => _store.ContainsKey(id);
            public T Load<T>(string id, T defaultValue = default) => defaultValue;
            public void Save<T>(string id, T value) =>
                _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            public void Delete(string id) => _store.Remove(id);
            public void Clear() { _store.Clear(); OnClear?.Invoke(); }

            void IBatchableSaveLoader.BeginBatch()  => BeginBatchCallCount++;
            void IBatchableSaveLoader.CommitBatch()  => CommitBatchCallCount++;
            void IBatchableSaveLoader.RollbackBatch() => RollbackBatchCallCount++;
        }

        private class ThrowingBatchableSaveLoader : SaveLoader, IBatchableSaveLoader
        {
            public event Action OnClear;
            public int RollbackBatchCallCount;

            public bool Has(string id) => false;
            public T Load<T>(string id, T defaultValue = default) => defaultValue;
            public void Save<T>(string id, T value) =>
                throw new InvalidOperationException("Simulated write failure");
            public void Delete(string id) { }
            public void Clear() { OnClear?.Invoke(); }

            void IBatchableSaveLoader.BeginBatch()   { }
            void IBatchableSaveLoader.CommitBatch()  { }
            void IBatchableSaveLoader.RollbackBatch() => RollbackBatchCallCount++;
        }

        private class FakeMainThreadSaveLoader : SaveLoader, IMainThreadSaveLoader
        {
            public event Action OnClear;
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public bool Has(string id) => _store.ContainsKey(id);
            public T Load<T>(string id, T defaultValue = default) => defaultValue;
            public void Save<T>(string id, T value) =>
                _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            public void Delete(string id) => _store.Remove(id);
            public void Clear() { _store.Clear(); OnClear?.Invoke(); }
        }

        /// <summary>
        /// A SaveLoader that blocks on the first Save() call until <see cref="Release"/> is
        /// called, and signals via <see cref="WaitUntilWriteStarted"/> that the block is active.
        /// Used to deterministically test concurrent <see cref="GameDataWriter.SaveAsync"/> calls.
        /// </summary>
        private class BlockingSaveLoader : SaveLoader
        {
            public event Action OnClear;
            private readonly ManualResetEventSlim _startedSignal = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _proceedGate   = new ManualResetEventSlim(false);
            private readonly Dictionary<string, string> _store   = new Dictionary<string, string>();
            private bool _firstSaveSeen;

            public bool Has(string id) => _store.ContainsKey(id);
            public T Load<T>(string id, T defaultValue = default) => defaultValue;

            public void Save<T>(string id, T value)
            {
                if (!_firstSaveSeen)
                {
                    _firstSaveSeen = true;
                    _startedSignal.Set();   // tell the test that the background write has begun
                    _proceedGate.Wait();    // wait until the test releases us
                }
                _store[id] = Newtonsoft.Json.JsonConvert.SerializeObject(value);
            }

            public void Delete(string id) => _store.Remove(id);
            public void Clear() { _store.Clear(); OnClear?.Invoke(); }

            /// <summary>Blocks until the first Save() call has been entered.</summary>
            public void WaitUntilWriteStarted() => _startedSignal.Wait(TimeSpan.FromSeconds(5));

            /// <summary>Unblocks the background writer so it can proceed.</summary>
            public void Release() => _proceedGate.Set();
        }

        // -----------------------------------------------------------------------
        // Helper
        // -----------------------------------------------------------------------

        private static GameDataWriter BuildWriter(
            SaveLoader saveLoader,
            string gameDataId = "testGame",
            int currentVersion = 1)
        {
            Mock<Resolver> r = new Mock<Resolver>();
            r.Setup(x => x.Resolve<SaveLoader>()).Returns(saveLoader);
            r.Setup(x => x.Resolve<GameDataWriter.Arguments>()).Returns(
                new GameDataWriter.Arguments { GameDataId = gameDataId, CurrentVersion = currentVersion });

            GameDataWriter writer = new GameDataWriter();
            ((Injectable)writer).Inject(r.Object);
            return writer;
        }

        private static List<(string Id, JToken Data)> MakeSnapshot(params string[] ids)
        {
            List<(string, JToken)> snapshot = new List<(string, JToken)>();
            foreach (string id in ids)
                snapshot.Add((id, JToken.FromObject(new { key = id })));
            return snapshot;
        }

        // -----------------------------------------------------------------------
        // CommitToStorage — non-batchable SaveLoader
        // -----------------------------------------------------------------------

        [Test]
        [Description("CommitToStorage with a plain SaveLoader => entries and version key written without batching")]
        public void CommitToStorage_NonBatchable_WritesEntriesAndVersionKey()
        {
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            writer.CommitToStorage(MakeSnapshot("alpha", "beta"));

            Assert.That(saveLoader.Has("alpha"), Is.True);
            Assert.That(saveLoader.Has("beta"), Is.True);
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
        }

        // -----------------------------------------------------------------------
        // CommitToStorage — batchable SaveLoader happy path
        // -----------------------------------------------------------------------

        [Test]
        [Description("CommitToStorage with a batchable SaveLoader => BeginBatch and CommitBatch each called once, data written")]
        public void CommitToStorage_Batchable_CallsBatchMethodsAndWritesData()
        {
            FakeBatchableSaveLoader saveLoader = new FakeBatchableSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            writer.CommitToStorage(MakeSnapshot("alpha"));

            Assert.That(saveLoader.BeginBatchCallCount,   Is.EqualTo(1));
            Assert.That(saveLoader.CommitBatchCallCount,  Is.EqualTo(1));
            Assert.That(saveLoader.RollbackBatchCallCount, Is.EqualTo(0));
            Assert.That(saveLoader.Has("alpha"), Is.True);
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
        }

        // -----------------------------------------------------------------------
        // CommitToStorage — batchable SaveLoader write failure triggers rollback
        // -----------------------------------------------------------------------

        [Test]
        [Description("CommitToStorage when Save() throws on a batchable SaveLoader => RollbackBatch called and exception propagated")]
        public void CommitToStorage_Batchable_WriteThrows_RollsBackAndRethrows()
        {
            ThrowingBatchableSaveLoader saveLoader = new ThrowingBatchableSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            Assert.Throws<InvalidOperationException>(() =>
                writer.CommitToStorage(MakeSnapshot("willThrow")));

            Assert.That(saveLoader.RollbackBatchCallCount, Is.EqualTo(1));
        }

        // -----------------------------------------------------------------------
        // SaveAsync — basic background write
        // -----------------------------------------------------------------------

        [Test]
        [Description("SaveAsync with a background-capable SaveLoader => entries and version key present after task completes")]
        public void SaveAsync_WritesDataToStorageAfterCompletion()
        {
            FakeSaveLoader saveLoader = new FakeSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            writer.SaveAsync(MakeSnapshot("gamma")).GetAwaiter().GetResult();

            Assert.That(saveLoader.Has("gamma"), Is.True);
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
        }

        // -----------------------------------------------------------------------
        // SaveAsync — IMainThreadSaveLoader falls back to synchronous write
        // -----------------------------------------------------------------------

        [Test]
        [Description("SaveAsync when the SaveLoader is IMainThreadSaveLoader => falls back to synchronous write, data present, warning logged")]
        public void SaveAsync_MainThreadSaveLoader_FallsBackToSyncAndLogsWarning()
        {
            FakeMainThreadSaveLoader saveLoader = new FakeMainThreadSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            LogAssert.Expect(LogType.Warning, new Regex("requires main-thread access"));
            writer.SaveAsync(MakeSnapshot("delta")).GetAwaiter().GetResult();

            Assert.That(saveLoader.Has("delta"), Is.True);
            Assert.That(saveLoader.Has(GameDataPersistence.VersionKey), Is.True);
        }

        [Test]
        [Description("SaveAsync called twice with IMainThreadSaveLoader => warning logged only once")]
        public void SaveAsync_MainThreadSaveLoader_WarningLoggedOnlyOnce()
        {
            FakeMainThreadSaveLoader saveLoader = new FakeMainThreadSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            LogAssert.Expect(LogType.Warning, new Regex("requires main-thread access"));

            // First call — warning expected (registered once above).
            writer.SaveAsync(MakeSnapshot("first")).GetAwaiter().GetResult();
            // Second call — no additional warning should fire.
            writer.SaveAsync(MakeSnapshot("second")).GetAwaiter().GetResult();
        }

        // -----------------------------------------------------------------------
        // SaveAsync — latest-wins concurrency policy
        // -----------------------------------------------------------------------

        [Test]
        [Description("SaveAsync called while a write is in flight => pending snapshot replaced; second snapshot's data present after flush")]
        public void SaveAsync_WhileWriteInFlight_LatestSnapshotCommitted()
        {
            BlockingSaveLoader saveLoader = new BlockingSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            // Start first write — background thread will block immediately.
            writer.SaveAsync(MakeSnapshot("snapshot1_key"));

            // Wait until the background write has actually started blocking.
            saveLoader.WaitUntilWriteStarted();

            // Queue a second snapshot while the first is in flight.
            writer.SaveAsync(MakeSnapshot("snapshot2_key"));

            // Unblock the background writer and wait for everything to complete.
            saveLoader.Release();
            writer.FlushPendingWrite();

            // The second snapshot must be in the store.
            Assert.That(saveLoader.Has("snapshot2_key"), Is.True);
        }

        // -----------------------------------------------------------------------
        // FlushPendingWrite — no pending write
        // -----------------------------------------------------------------------

        [Test]
        [Description("FlushPendingWrite when no write is in progress => returns immediately without throwing")]
        public void FlushPendingWrite_NoPendingWrite_DoesNotThrow()
        {
            GameDataWriter writer = BuildWriter(new FakeSaveLoader());

            Assert.DoesNotThrow(() => writer.FlushPendingWrite());
        }

        // -----------------------------------------------------------------------
        // FlushPendingWrite — write in flight
        // -----------------------------------------------------------------------

        [Test]
        [Description("FlushPendingWrite while a background write is in flight => blocks until write completes and data is present")]
        public void FlushPendingWrite_WriteInFlight_BlocksUntilComplete()
        {
            BlockingSaveLoader saveLoader = new BlockingSaveLoader();
            GameDataWriter writer = BuildWriter(saveLoader);

            writer.SaveAsync(MakeSnapshot("epsilon"));
            saveLoader.WaitUntilWriteStarted();

            // Release on a separate thread so the test is not deadlocked.
            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(50);
                saveLoader.Release();
            });

            writer.FlushPendingWrite();

            Assert.That(saveLoader.Has("epsilon"), Is.True);
        }
    }
}
