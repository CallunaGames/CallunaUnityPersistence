using Calluna.DI;
using Calluna.Persistence;
using Moq;
using NUnit.Framework;
using UnityEngine;

namespace Calluna.Template.Tests
{
    /// <summary>
    /// Tests for <see cref="PlayerPrefsSaveLoader"/>.
    /// Each test isolates PlayerPrefs state via SetUp/TearDown DeleteAll calls.
    /// </summary>
    [TestFixture]
    public class PlayerPrefsSaveLoaderTests
    {
        private PlayerPrefsSaveLoader _loader;

        private class ComplexObject
        {
            public string Label { get; set; }
            public int Value { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteAll();
            _loader = BuildLoader();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteAll();
        }

        private static PlayerPrefsSaveLoader BuildLoader()
        {
            Mock<Resolver> resolverMock = new Mock<Resolver>();

            // Build a real JsonSerializer with default settings.
            Mock<Resolver> serializerResolver = new Mock<Resolver>();
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>())
                .Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            serializerResolver.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializer>())
                .Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(serializerResolver.Object);

            resolverMock.Setup(r => r.Resolve<JsonSerializer>()).Returns(serializer);

            PlayerPrefsSaveLoader loader = new PlayerPrefsSaveLoader();
            ((Injectable)loader).Inject(resolverMock.Object);
            return loader;
        }

        // -----------------------------------------------------------------------
        // Has
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("key_has_after_save_1")]
        [TestCase("key_has_after_save_2")]
        [Description("Has() after Save() => returns true?")]
        public void Has_AfterSave_ReturnsTrue(string key)
        {
            _loader.Save(key, 99);

            Assert.That(_loader.Has(key), Is.True);
        }

        [Test]
        [TestCase("unknown_key_a")]
        [TestCase("unknown_key_b")]
        [Description("Has() for unknown key => returns false?")]
        public void Has_UnknownKey_ReturnsFalse(string key)
        {
            Assert.That(_loader.Has(key), Is.False);
        }

        // -----------------------------------------------------------------------
        // Load<int>
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("int_key_1", 0)]
        [TestCase("int_key_2", 42)]
        [TestCase("int_key_3", -100)]
        [Description("Load<int>() after Save<int>() => returns saved int?")]
        public void Load_Int_ReturnsSavedInt(string key, int value)
        {
            _loader.Save(key, value);

            int result = _loader.Load<int>(key);

            Assert.That(result, Is.EqualTo(value));
        }

        // -----------------------------------------------------------------------
        // Load<float>
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("float_key_1", 0f)]
        [TestCase("float_key_2", 3.14f)]
        [TestCase("float_key_3", -1.5f)]
        [Description("Load<float>() after Save<float>() => returns saved float?")]
        public void Load_Float_ReturnsSavedFloat(string key, float value)
        {
            _loader.Save(key, value);

            float result = _loader.Load<float>(key);

            Assert.That(result, Is.EqualTo(value).Within(0.0001f));
        }

        // -----------------------------------------------------------------------
        // Load<bool>
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("bool_true_key")]
        [TestCase("bool_true_key_2")]
        [Description("Save true then Load<bool>() => returns true?")]
        public void Load_Bool_True_ReturnsTrue(string key)
        {
            _loader.Save(key, true);

            bool result = _loader.Load<bool>(key);

            Assert.That(result, Is.True);
        }

        [Test]
        [TestCase("bool_false_key")]
        [TestCase("bool_false_key_2")]
        [Description("Save false then Load<bool>() => returns false?")]
        public void Load_Bool_False_ReturnsFalse(string key)
        {
            _loader.Save(key, false);

            bool result = _loader.Load<bool>(key);

            Assert.That(result, Is.False);
        }

        // -----------------------------------------------------------------------
        // Load<string>
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("string_key_1", "hello")]
        [TestCase("string_key_2", "world")]
        [TestCase("string_key_3", "")]
        [Description("Load<string>() after Save<string>() => returns saved string?")]
        public void Load_String_ReturnsSavedString(string key, string value)
        {
            _loader.Save(key, value);

            string result = _loader.Load<string>(key);

            Assert.That(result, Is.EqualTo(value));
        }

        // -----------------------------------------------------------------------
        // Load<T> — complex object
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("complex_key_1", "Alpha", 1)]
        [TestCase("complex_key_2", "Beta", 99)]
        [Description("Load<T>() for complex object after Save => deserializes via JsonSerializer?")]
        public void Load_ComplexObject_DeserializesCorrectly(string key, string label, int value)
        {
            ComplexObject original = new ComplexObject { Label = label, Value = value };
            _loader.Save(key, original);

            ComplexObject result = _loader.Load<ComplexObject>(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Label, Is.EqualTo(label));
            Assert.That(result.Value, Is.EqualTo(value));
        }

        // -----------------------------------------------------------------------
        // Load<T> — missing key returns default
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("missing_int_key", 77)]
        [TestCase("missing_int_key_2", -3)]
        [Description("Load<T>() for missing key => returns provided default value?")]
        public void Load_MissingKey_ReturnsDefault(string key, int defaultValue)
        {
            int result = _loader.Load<int>(key, defaultValue);

            Assert.That(result, Is.EqualTo(defaultValue));
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

        [Test]
        [TestCase("never_saved_key_a")]
        [TestCase("never_saved_key_b")]
        [Description("Delete() on a key that was never saved => no exception thrown, Has() still returns false?")]
        public void Delete_NeverSavedKey_DoesNotThrowAndHasReturnsFalse(string key)
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
        [Description("Clear() => all keys removed, Has() returns false for previously saved key?")]
        public void Clear_RemovesAllKeys(string key)
        {
            _loader.Save(key, 10);

            _loader.Clear();

            Assert.That(_loader.Has(key), Is.False);
        }

        [Test]
        [TestCase("clear_event_key_1")]
        [TestCase("clear_event_key_2")]
        [Description("Clear() => OnClear event is raised exactly once?")]
        public void Clear_RaisesOnClearExactlyOnce(string key)
        {
            _loader.Save(key, 5);
            int callCount = 0;
            _loader.OnClear += OnClearHandler;

            _loader.Clear();

            Assert.That(callCount, Is.EqualTo(1));
            _loader.OnClear -= OnClearHandler;
            return;

            void OnClearHandler() => callCount++;
        }
    }
}
