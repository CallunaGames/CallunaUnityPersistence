using Calluna.DI;
using Calluna.Persistence;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Calluna.Template.Tests
{
    [TestFixture]
    public class JsonSerializerTests
    {
        private JsonSerializer _serializer;

        // A small POCO used as a complex-type representative throughout these tests.
        private class SampleData
        {
            public string Name { get; set; }
            public int Score { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _serializer = BuildSerializer();
        }

        // -----------------------------------------------------------------------
        // Helper
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a JsonSerializer injected via a Resolver stub that returns null for
        /// all optional overrides so the class uses its own defaults.
        /// </summary>
        private static JsonSerializer BuildSerializer()
        {
            Mock<Resolver> resolverMock = new Mock<Resolver>();
            resolverMock.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializerSettings>()).Returns((Newtonsoft.Json.JsonSerializerSettings)null);
            resolverMock.Setup(r => r.ResolveOptional<Newtonsoft.Json.JsonSerializer>()).Returns((Newtonsoft.Json.JsonSerializer)null);

            JsonSerializer serializer = new JsonSerializer();
            ((Injectable)serializer).Inject(resolverMock.Object);
            return serializer;
        }

        // -----------------------------------------------------------------------
        // Serialize<T>(T value) — string output
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("Alice", 42)]
        [TestCase("Bob", 0)]
        [TestCase("", -1)]
        [Description("Serialize<T>() simple object => produced JSON contains expected field names and values?")]
        public void Serialize_SimpleObject_ContainsExpectedFields(string name, int score)
        {
            SampleData data = new SampleData { Name = name, Score = score };

            string json = _serializer.Serialize(data);

            Assert.That(json, Does.Contain($"\"{score}\"").Or.Contain($":{score}").Or.Contain($":{score},"));
            Assert.That(json, Does.Contain("Name").Or.Contain("name"));
            Assert.That(json, Does.Contain("Score").Or.Contain("score"));
        }

        [Test]
        [Description("Serialize<T>() null => produces 'null' string?")]
        public void Serialize_Null_ProducesNullString()
        {
            string result = _serializer.Serialize<SampleData>(null);

            Assert.That(result, Is.EqualTo("null"));
        }

        // -----------------------------------------------------------------------
        // SerializeToToken<T>(T value) — JToken output
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("Alice", 100)]
        [TestCase("Charlie", 999)]
        [Description("SerializeToToken<T>() simple object => JToken has correct property values?")]
        public void SerializeToToken_SimpleObject_HasCorrectProperties(string name, int score)
        {
            SampleData data = new SampleData { Name = name, Score = score };

            JToken token = _serializer.SerializeToToken(data);

            Assert.That(token["Name"]?.Value<string>(), Is.EqualTo(name));
            Assert.That(token["Score"]?.Value<int>(), Is.EqualTo(score));
        }

        // -----------------------------------------------------------------------
        // Deserialize<T>(string) — from string
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("{\"Name\":\"Alice\",\"Score\":42}", "Alice", 42)]
        [TestCase("{\"Name\":\"Bob\",\"Score\":7}", "Bob", 7)]
        [Description("Deserialize<T>(string) valid JSON => returns populated object?")]
        public void Deserialize_ValidJsonString_ReturnsPopulatedObject(string json, string expectedName, int expectedScore)
        {
            SampleData result = _serializer.Deserialize<SampleData>(json);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(expectedName));
            Assert.That(result.Score, Is.EqualTo(expectedScore));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [Description("Deserialize<T>(string) null or empty => returns null or default without throwing?")]
        public void Deserialize_NullOrEmptyString_ReturnsNullWithoutThrowing(string input)
        {
            SampleData result = null;
            Assert.DoesNotThrow(() => result = _serializer.Deserialize<SampleData>(input));
            Assert.That(result, Is.Null);
        }

        // -----------------------------------------------------------------------
        // Deserialize<T>(JToken) — from JToken
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("Diana", 55)]
        [TestCase("Eve", 0)]
        [Description("Deserialize<T>(JToken) for JObject => returns populated object?")]
        public void Deserialize_JObject_ReturnsPopulatedObject(string name, int score)
        {
            JObject jObject = JObject.FromObject(new SampleData { Name = name, Score = score });

            SampleData result = _serializer.Deserialize<SampleData>(jObject);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(name));
            Assert.That(result.Score, Is.EqualTo(score));
        }

        [Test]
        [TestCase("{\"Name\":\"Frank\",\"Score\":3}")]
        [TestCase("{\"Name\":\"Grace\",\"Score\":77}")]
        [Description("Deserialize<T>(JToken) for JValue with string type => round-trips through string overload correctly?")]
        public void Deserialize_JValueString_RoundTripsThroughStringOverload(string serializedJson)
        {
            JValue jValue = new JValue(serializedJson);

            SampleData result = _serializer.Deserialize<SampleData>(jValue);

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        [TestCase(42)]
        [TestCase(-7)]
        [TestCase(0)]
        [Description("Deserialize<T>(JToken) for JValue with numeric type => returns correct value directly?")]
        public void Deserialize_JValueNumeric_ReturnsCorrectValue(int numericValue)
        {
            JValue jValue = new JValue(numericValue);

            int result = _serializer.Deserialize<int>(jValue);

            Assert.That(result, Is.EqualTo(numericValue));
        }

        // -----------------------------------------------------------------------
        // Round-trip: Serialize then Deserialize
        // -----------------------------------------------------------------------

        [Test]
        [TestCase("RoundTrip", 123)]
        [TestCase("Another", -5)]
        [Description("Serialize then Deserialize<T>(string) => original values are preserved?")]
        public void SerializeDeserialize_RoundTrip_PreservesValues(string name, int score)
        {
            SampleData original = new SampleData { Name = name, Score = score };

            string json = _serializer.Serialize(original);
            SampleData result = _serializer.Deserialize<SampleData>(json);

            Assert.That(result.Name, Is.EqualTo(original.Name));
            Assert.That(result.Score, Is.EqualTo(original.Score));
        }
    }
}
