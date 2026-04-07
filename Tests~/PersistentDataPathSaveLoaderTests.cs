using System;
using System.IO;
using Calluna.DI;
using Calluna.Persistence;
using Moq;
using NUnit.Framework;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Tests for <see cref="PersistentDataPathSaveLoader"/>.
    /// Each test uses a unique temp file path so tests are fully isolated.
    /// The file is deleted in TearDown.
    /// </summary>
    [TestFixture]
    public class PersistentDataPathSaveLoaderTests
    {
        private string _tempFilePath;
        private PersistentDataPathSaveLoader _loader;

        private class SamplePayload
        {
            public string Tag { get; set; }
            public int Count { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"CallunaTest_{Guid.NewGuid():N}.json");
            _loader = BuildLoader(_tempFilePath);
        }

        [TearDown]
        public void TearDown()
        {
            // Ensure streams are closed before deletion.
            try { ((Calluna.DI.Cleanable)_loader).Clean(); } catch { /* ignore */ }
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        // -----------------------------------------------------------------------
        // Helper
        // -----------------------------------------------------------------------

        private static PersistentDataPathSaveLoader BuildLoader(string fullPath)
        {
            Mock<Resolver> serializerResolver = new Mock<Resolver>();
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(serializerResolver.Object);

            // The loader concatenates Application.persistentDataPath + FileName.
            // We use only the file name and accept the path will land in
            // Application.persistentDataPath, which IS accessible in edit-mode tests.
            string fileName = Path.GetFileName(fullPath);

            TextFileReadWriter readWriter = new TextFileReadWriter();

            Mock<Resolver> loaderResolver = new Mock<Resolver>();
            loaderResolver.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);
            loaderResolver.Setup(r => r.Resolve<TextFileReadWriter>()).Returns(readWriter);
            loaderResolver.Setup(r => r.Resolve<PersistentDataPathSaveLoader.Arguments>())
                .Returns(new PersistentDataPathSaveLoader.Arguments { FileName = fileName });

            PersistentDataPathSaveLoader loader = new PersistentDataPathSaveLoader();
            ((Injectable)loader).Inject(loaderResolver.Object);
            return loader;
        }

        /// <summary>
        /// Returns the actual file path the loader will use (inside persistentDataPath).
        /// </summary>
        private string ActualPath => Path.Combine(
            UnityEngine.Application.persistentDataPath,
            Path.GetFileName(_tempFilePath));

        // -----------------------------------------------------------------------
        // Has
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("has_missing_a")]
        [TestCase("has_missing_b")]
        [Description("Has() for missing key => returns false?")]
        public void Has_MissingKey_ReturnsFalse(string key)
        {
            Assert.That(_loader.Has(key), Is.False);
        }

        [Test]
        [TestCase("has_after_save_1")]
        [TestCase("has_after_save_2")]
        [Description("Has() after Save() => returns true?")]
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
        [Description("Load<T>() for missing key => returns default value?")]
        public void Load_MissingKey_ReturnsDefaultValue(string key, string defaultValue)
        {
            string result = _loader.Load<string>(key, defaultValue);

            Assert.That(result, Is.EqualTo(defaultValue));
        }

        [Test]
        [TestCase("roundtrip_1", "Omega", 7)]
        [TestCase("roundtrip_2", "Lambda", -3)]
        [Description("Load<T>() after Save<T>() => returns correct deserialized value (round-trip)?")]
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
        // Cross-instance persistence
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("persist_key_1", "Gamma", 100)]
        [TestCase("persist_key_2", "Delta", 200)]
        [Description("Save<T>() persists across a second instance reading the same file?")]
        public void Save_PersistsAcrossNewInstance(string key, string tag, int count)
        {
            SamplePayload original = new SamplePayload { Tag = tag, Count = count };
            _loader.Save(key, original);

            // Close streams so the second instance can open the file.
            ((Calluna.DI.Cleanable)_loader).Clean();

            PersistentDataPathSaveLoader secondLoader = BuildLoader(_tempFilePath);
            SamplePayload result = secondLoader.Load<SamplePayload>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tag, Is.EqualTo(tag));
            Assert.That(result.Count, Is.EqualTo(count));

            ((Calluna.DI.Cleanable)secondLoader).Clean();
            // Clean up the file created in the actual persistentDataPath.
            if (File.Exists(ActualPath))
                File.Delete(ActualPath);
        }

        // -----------------------------------------------------------------------
        // Delete
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("delete_key_1")]
        [TestCase("delete_key_2")]
        [Description("Delete() after Save() => Has() returns false?")]
        public void Delete_AfterSave_HasReturnsFalse(string key)
        {
            _loader.Save(key, 1);

            _loader.Delete(key);

            Assert.That(_loader.Has(key), Is.False);
        }

        // -----------------------------------------------------------------------
        // Clear
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("clear_key_1")]
        [TestCase("clear_key_2")]
        [Description("Clear() => file is deleted, in-memory state reset, Has() returns false for previously saved key?")]
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
        [Description("Clear() => OnClear event raised exactly once?")]
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
        // Delete — absent key
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("delete_absent_1")]
        [TestCase("delete_absent_2")]
        [Description("Delete() on a key that was never saved => no exception thrown, Has() still returns false?")]
        public void Delete_AbsentKey_NoExceptionAndHasReturnsFalse(string key)
        {
            Assert.DoesNotThrow(() => _loader.Delete(key));
            Assert.That(_loader.Has(key), Is.False);
        }

        // -----------------------------------------------------------------------
        // Load — primitive round-trips via JToken
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("prim_int_1", 0)]
        [TestCase("prim_int_2", 42)]
        [TestCase("prim_int_3", -7)]
        [Description("Save<int>() then Load<int>() => primitive int round-trips correctly via JToken?")]
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
        [Description("Save<float>() then Load<float>() => primitive float round-trips correctly via JToken?")]
        public void Load_Float_RoundTrip_ReturnsOriginalValue(string key, float value)
        {
            _loader.Save(key, value);

            float result = _loader.Load<float>(key);

            Assert.That(result, Is.EqualTo(value).Within(0.0001f));
        }

        [Test]
        [TestCase("prim_bool_true_1")]
        [TestCase("prim_bool_true_2")]
        [Description("Save<bool>(true) then Load<bool>() => true round-trips correctly via JToken?")]
        public void Load_BoolTrue_RoundTrip_ReturnsTrue(string key)
        {
            _loader.Save(key, true);

            bool result = _loader.Load<bool>(key);

            Assert.That(result, Is.True);
        }

        [Test]
        [TestCase("prim_bool_false_1")]
        [TestCase("prim_bool_false_2")]
        [Description("Save<bool>(false) then Load<bool>() => false round-trips correctly via JToken?")]
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
        [Description("Save<string>() then Load<string>() => primitive string round-trips correctly via JToken?")]
        public void Load_String_RoundTrip_ReturnsOriginalValue(string key, string value)
        {
            _loader.Save(key, value);

            string result = _loader.Load<string>(key);

            Assert.That(result, Is.EqualTo(value));
        }

        // -----------------------------------------------------------------------
        // Clean (stream management)
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("clean_key_1", 11)]
        [TestCase("clean_key_2", 22)]
        [Description("Clean() => closes streams without throwing; subsequent Save() reopens streams correctly?")]
        public void Clean_ClosesThenSubsequentSaveSucceeds(string key, int value)
        {
            // Force streams to open by performing a save.
            _loader.Save(key, value);

            // Clean — should not throw.
            Assert.DoesNotThrow(() => ((Calluna.DI.Cleanable)_loader).Clean());

            // Save again after clean — streams should reopen automatically.
            Assert.DoesNotThrow(() => _loader.Save(key, value + 1));

            int result = _loader.Load<int>(key);
            Assert.That(result, Is.EqualTo(value + 1));
        }
    }
}
