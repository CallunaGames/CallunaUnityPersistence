using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Calluna.DI;
using Calluna.Persistence;
using Moq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Tests for <see cref="SqliteSaveLoader"/>.
    /// Each test uses a unique temp database file so tests are fully isolated.
    /// The file (and any WAL / SHM journals) is deleted in TearDown.
    /// </summary>
    [TestFixture]
    public class SqliteSaveLoaderTests
    {
        private string _tempFileName;
        private SqliteSaveLoader _loader;

        private class SamplePayload
        {
            public string Tag { get; set; }
            public int Count { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _tempFileName = $"CallunaTest_{Guid.NewGuid():N}.db";
            _loader = BuildLoader(_tempFileName);
        }

        [TearDown]
        public void TearDown()
        {
            try { ((Cleanable)_loader).Clean(); } catch { /* ignore */ }
            DeleteIfExists(ActualPath);
            DeleteIfExists(ActualPath + "-wal");
            DeleteIfExists(ActualPath + "-shm");
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static SqliteSaveLoader BuildLoader(string fileName)
        {
            Mock<Resolver> serializerResolver = new Mock<Resolver>();
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(serializerResolver.Object);

            Mock<Resolver> loaderResolver = new Mock<Resolver>();
            loaderResolver.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);
            loaderResolver.Setup(r => r.Resolve<SqliteSaveLoader.Arguments>())
                .Returns(new SqliteSaveLoader.Arguments { FileName = fileName });

            SqliteSaveLoader loader = new SqliteSaveLoader();
            ((Injectable)loader).Inject(loaderResolver.Object);
            return loader;
        }

        private string ActualPath =>
            Path.Combine(UnityEngine.Application.persistentDataPath, _tempFileName);

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        // -----------------------------------------------------------------------
        // Has
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("has_missing_a")]
        [TestCase("has_missing_b")]
        [Description("Has() for a key that was never saved => returns false")]
        public void Has_MissingKey_ReturnsFalse(string key)
        {
            Assert.That(_loader.Has(key), Is.False);
        }

        [Test]
        [TestCase("has_after_save_1")]
        [TestCase("has_after_save_2")]
        [Description("Has() after Save() => returns true")]
        public void Has_AfterSave_ReturnsTrue(string key)
        {
            _loader.Save(key, 42);

            Assert.That(_loader.Has(key), Is.True);
        }

        // -----------------------------------------------------------------------
        // Load
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("load_missing_1", "default_a")]
        [TestCase("load_missing_2", "default_b")]
        [Description("Load<T>() for a missing key => returns the provided default value")]
        public void Load_MissingKey_ReturnsDefaultValue(string key, string defaultValue)
        {
            string result = _loader.Load<string>(key, defaultValue);

            Assert.That(result, Is.EqualTo(defaultValue));
        }

        [Test]
        [TestCase("roundtrip_1", "Omega", 7)]
        [TestCase("roundtrip_2", "Lambda", -3)]
        [Description("Load<T>() after Save<T>() => returns correct deserialized value (round-trip)")]
        public void Load_AfterSave_ReturnsCorrectValue(string key, string tag, int count)
        {
            SamplePayload original = new SamplePayload { Tag = tag, Count = count };
            _loader.Save(key, original);

            SamplePayload result = _loader.Load<SamplePayload>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tag, Is.EqualTo(tag));
            Assert.That(result.Count, Is.EqualTo(count));
        }

        // -----------------------------------------------------------------------
        // Primitive round-trips
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("prim_int_1", 0)]
        [TestCase("prim_int_2", 42)]
        [TestCase("prim_int_3", -7)]
        [Description("Save<int>() then Load<int>() => primitive int round-trips correctly")]
        public void Load_Int_RoundTrip_ReturnsOriginalValue(string key, int value)
        {
            _loader.Save(key, value);

            int result = _loader.Load<int>(key);

            Assert.That(result, Is.EqualTo(value));
        }

        [Test]
        [TestCase("prim_float_1", 0f)]
        [TestCase("prim_float_2", 3.14f)]
        [TestCase("prim_float_3", -1.5f)]
        [Description("Save<float>() then Load<float>() => primitive float round-trips correctly")]
        public void Load_Float_RoundTrip_ReturnsOriginalValue(string key, float value)
        {
            _loader.Save(key, value);

            float result = _loader.Load<float>(key);

            Assert.That(result, Is.EqualTo(value).Within(0.0001f));
        }

        [Test]
        [TestCase("prim_bool_true_1")]
        [TestCase("prim_bool_true_2")]
        [Description("Save<bool>(true) then Load<bool>() => true round-trips correctly")]
        public void Load_BoolTrue_RoundTrip_ReturnsTrue(string key)
        {
            _loader.Save(key, true);

            bool result = _loader.Load<bool>(key);

            Assert.That(result, Is.True);
        }

        [Test]
        [TestCase("prim_bool_false_1")]
        [TestCase("prim_bool_false_2")]
        [Description("Save<bool>(false) then Load<bool>() => false round-trips correctly")]
        public void Load_BoolFalse_RoundTrip_ReturnsFalse(string key)
        {
            _loader.Save(key, false);

            bool result = _loader.Load<bool>(key);

            Assert.That(result, Is.False);
        }

        [Test]
        [TestCase("prim_str_1", "hello")]
        [TestCase("prim_str_2", "world")]
        [TestCase("prim_str_3", "")]
        [Description("Save<string>() then Load<string>() => primitive string round-trips correctly")]
        public void Load_String_RoundTrip_ReturnsOriginalValue(string key, string value)
        {
            _loader.Save(key, value);

            string result = _loader.Load<string>(key);

            Assert.That(result, Is.EqualTo(value));
        }

        // -----------------------------------------------------------------------
        // Cross-instance persistence
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("persist_key_1", "Gamma", 100)]
        [TestCase("persist_key_2", "Delta", 200)]
        [Description("Save<T>() persists data so a second loader instance reading the same file sees it")]
        public void Save_PersistsAcrossNewInstance(string key, string tag, int count)
        {
            SamplePayload original = new SamplePayload { Tag = tag, Count = count };
            _loader.Save(key, original);

            // Close connection so the second instance can open the file cleanly.
            ((Cleanable)_loader).Clean();

            SqliteSaveLoader secondLoader = BuildLoader(_tempFileName);
            SamplePayload result = secondLoader.Load<SamplePayload>(key);
            ((Cleanable)secondLoader).Clean();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tag, Is.EqualTo(tag));
            Assert.That(result.Count, Is.EqualTo(count));
        }

        // -----------------------------------------------------------------------
        // Delete
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("delete_key_1")]
        [TestCase("delete_key_2")]
        [Description("Delete() after Save() => Has() returns false")]
        public void Delete_AfterSave_HasReturnsFalse(string key)
        {
            _loader.Save(key, 1);

            _loader.Delete(key);

            Assert.That(_loader.Has(key), Is.False);
        }

        [Test]
        [TestCase("delete_absent_1")]
        [TestCase("delete_absent_2")]
        [Description("Delete() on a key that was never saved => no exception thrown, Has() still returns false")]
        public void Delete_AbsentKey_NoExceptionAndHasReturnsFalse(string key)
        {
            Assert.DoesNotThrow(() => _loader.Delete(key));
            Assert.That(_loader.Has(key), Is.False);
        }

        // -----------------------------------------------------------------------
        // Clear
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("clear_key_1")]
        [TestCase("clear_key_2")]
        [Description("Clear() => database file is deleted, Has() returns false for a previously saved key")]
        public void Clear_DeletesFileAndResetsState(string key)
        {
            _loader.Save(key, 5);

            _loader.Clear();

            Assert.That(_loader.Has(key), Is.False);
            Assert.That(File.Exists(ActualPath), Is.False);
        }

        [Test]
        [TestCase("clear_event_1")]
        [TestCase("clear_event_2")]
        [Description("Clear() => OnClear event raised exactly once")]
        public void Clear_RaisesOnClearExactlyOnce(string key)
        {
            _loader.Save(key, 10);
            int callCount = 0;
            _loader.OnClear += OnClearHandler;

            _loader.Clear();

            Assert.That(callCount, Is.EqualTo(1));
            _loader.OnClear -= OnClearHandler;
            return;

            void OnClearHandler() => callCount++;
        }

        // -----------------------------------------------------------------------
        // Clean (connection management)
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("clean_key_1", 11)]
        [TestCase("clean_key_2", 22)]
        [Description("Clean() closes the connection without throwing; a subsequent Save() reopens it correctly")]
        public void Clean_ClosesThenSubsequentSaveSucceeds(string key, int value)
        {
            _loader.Save(key, value);

            Assert.DoesNotThrow(() => ((Cleanable)_loader).Clean());

            Assert.DoesNotThrow(() => _loader.Save(key, value + 1));

            int result = _loader.Load<int>(key);
            Assert.That(result, Is.EqualTo(value + 1));
        }

        // -----------------------------------------------------------------------
        // Clean — must not close the connection out from under an in-flight operation
        // -----------------------------------------------------------------------

        [Test]
        [Description("Clean() called while an operation holds the connection's read lock => blocks until the " +
                      "lock is released, then closes without throwing. Regression test for the shutdown race " +
                      "where DI cleanup order let Clean() close the connection while a background " +
                      "GameDataWriter.SaveAsync() write was still using it (SQLiteException: bad parameter or " +
                      "other API misuse).")]
        public void Clean_WhileOperationHoldsReadLock_WaitsForReleaseThenCloses()
        {
            // Open the connection before grabbing the lock ourselves.
            _loader.Save("warmup", 1);

            // Simulate an in-flight Save()/Load()/batch call the same way BlockingSaveLoader
            // simulates one in GameDataWriterTests — by holding the read lock open.
            _loader._connectionLock.EnterReadLock();
            try
            {
                Task cleanTask = Task.Run(() => ((Cleanable)_loader).Clean());

                // While the simulated operation still holds the lock, Clean() must not complete.
                Assert.That(cleanTask.Wait(TimeSpan.FromMilliseconds(300)), Is.False,
                    "Clean() completed while an operation still held the read lock.");

                _loader._connectionLock.ExitReadLock();

                Assert.That(cleanTask.Wait(TimeSpan.FromSeconds(5)), Is.True,
                    "Clean() did not complete shortly after the read lock was released.");
            }
            finally
            {
                if (_loader._connectionLock.IsReadLockHeld)
                    _loader._connectionLock.ExitReadLock();
            }

            Assert.DoesNotThrow(() => _loader.Save("warmup", 2));
        }

        [Test]
        [Description("Clean() whose wait for the write lock times out (e.g. a stuck operation) => logs a " +
                      "warning and closes the connection anyway instead of hanging the app on quit forever.")]
        public void Clean_LockWaitTimesOut_LogsWarningAndClosesAnyway()
        {
            _loader.Save("warmup", 1);

            TimeSpan original = SqliteSaveLoader.CleanLockTimeout;
            SqliteSaveLoader.CleanLockTimeout = TimeSpan.FromMilliseconds(100);

            // The read lock must be held by a *different* thread than the one calling Clean():
            // ReaderWriterLockSlim is thread-affine, so a single thread holding a read lock and
            // then requesting a write lock is an invalid upgrade (throws), not a timeout.
            ManualResetEventSlim lockHeldSignal = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseGate = new ManualResetEventSlim(false);
            Task holderTask = Task.Run(() =>
            {
                _loader._connectionLock.EnterReadLock();
                lockHeldSignal.Set();
                releaseGate.Wait(TimeSpan.FromSeconds(5));
                _loader._connectionLock.ExitReadLock();
            });

            try
            {
                Assert.That(lockHeldSignal.Wait(TimeSpan.FromSeconds(5)), Is.True,
                    "Background thread never signalled that it acquired the read lock.");

                LogAssert.Expect(LogType.Warning, new Regex("timed out waiting"));
                Assert.DoesNotThrow(() => ((Cleanable)_loader).Clean());
            }
            finally
            {
                releaseGate.Set();
                holderTask.Wait(TimeSpan.FromSeconds(5));
                SqliteSaveLoader.CleanLockTimeout = original;
            }

            // Clean() closed _connection despite the timeout, so this reopens it.
            Assert.DoesNotThrow(() => _loader.Save("warmup", 2));
        }
    }
}
