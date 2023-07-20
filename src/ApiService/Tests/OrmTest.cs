using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Data.Tables;
using FluentAssertions;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Moq;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Tests {

    public sealed class TestSecretOperations : ISecretsOperations {

        private readonly ConcurrentDictionary<Guid, string> _secrets = new();

        public Task<T?> GetSecretValue<T>(ISecret<T> data) where T : class {
            switch (data) {
                case SecretAddress<T> secretAddress:
                    var key = Guid.Parse(secretAddress.Url.Authority);
                    return Task.FromResult(_secrets.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value, EntityConverter.GetJsonSerializerOptions()) : default);
                case SecretValue<T> secretValue:
                    var x = secretValue.Value;
                    return Task.FromResult(x ?? default);
                default:
                    throw new ArgumentOutOfRangeException(nameof(data));
            }
        }

        public Task<Uri> StoreSecret(ISecret secret) {
            if (secret.IsHIddden) {
                return Task.FromResult(secret.Uri!);
            }

            var key = Guid.NewGuid();
            var address = new Uri($"https://{key}");

            _secrets[key] = secret?.GetValue() ?? "";
            return Task.FromResult(address);
        }

        public Task DeleteSecret(ISecret secret) {
            if (secret.Uri != null) {
                var key = Guid.Parse(secret.Uri.Authority);
                _ = _secrets.Remove(key, out _);
            }
            return Task.CompletedTask;
        }
    }

    public class OrmTest {
        sealed class TestObject {
            public String? TheName { get; set; }
            public TestEnum TheEnum { get; set; }
            public TestFlagEnum TheFlag { get; set; }
            public TestEnumValue TheEnumValue { get; set; }
        }

        enum TestEnum {
            TheOne,
            TheTwo,
        }

        [Flags]
        enum TestFlagEnum {
            FlagOne = 1,
            FlagTwo = 2,
        }

        [SerializeValue]
        enum TestEnumValue {
            One = 1,
            Two = 2
        }

        sealed record Entity1(
            [PartitionKey] Guid Id,
            [RowKey] string TheName,
            DateTimeOffset TheDate,
            int TheNumber,
            double TheFloat,
            TestEnum TheEnum,
            TestFlagEnum TheFlag,
            [property: JsonPropertyName("a__special__name")] string Renamed,
            TestObject TheObject,
            TestObject? TestNull,

            Uri TestUri,
            Uri? TestUriNull

            ) : EntityBase();


        [Fact]
        public void TestBothDirections() {
            var uriString = new Uri("https://localhost:9090");
            var converter = GetEntityConverter();
            var entity1 = new Entity1(
                            Guid.NewGuid(),
                            "test",
                            DateTimeOffset.UtcNow,
                            123,
                            12.44,
                            TestEnum.TheTwo, TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                            "renamed",
                            new TestObject {
                                TheName = "testobject",
                                TheEnum = TestEnum.TheTwo,
                                TheFlag = TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                                TheEnumValue = TestEnumValue.Two
                            },
                            null,
                            uriString,
                            null
                            );


            var tableEntity = converter.ToTableEntity(entity1).Result;
            var fromTableEntity = converter.ToRecord<Entity1>(tableEntity);
            var eq = fromTableEntity == entity1;

            Assert.Equal(fromTableEntity.Timestamp, entity1.Timestamp);
            Assert.Equal(fromTableEntity.Id, entity1.Id);
            Assert.Equal(fromTableEntity.Renamed, entity1.Renamed);
            Assert.Equal(fromTableEntity.TestNull, entity1.TestNull);
            Assert.Equal(fromTableEntity.TestUri, entity1.TestUri);
            Assert.Equal(fromTableEntity.TestUriNull, entity1.TestUriNull);
            Assert.Equal(fromTableEntity.TheDate, entity1.TheDate);
            Assert.Equal(fromTableEntity.TheEnum, entity1.TheEnum);

            Assert.Equal(fromTableEntity.TheFlag, entity1.TheFlag);
            Assert.Equal(fromTableEntity.TheFloat, entity1.TheFloat);
            Assert.Equal(fromTableEntity.TheName, entity1.TheName);
            Assert.Equal(fromTableEntity.TheNumber, entity1.TheNumber);
            Assert.Equal(fromTableEntity.Timestamp, entity1.Timestamp);

            Assert.Equal(fromTableEntity.TheObject.TheEnum, entity1.TheObject.TheEnum);
            Assert.Equal(fromTableEntity.TheObject.TheFlag, entity1.TheObject.TheFlag);
            Assert.Equal(fromTableEntity.TheObject.TheName, entity1.TheObject.TheName);
            Assert.Equal(fromTableEntity.TheObject.TheEnumValue, entity1.TheObject.TheEnumValue);
        }


        [Fact]
        public void TestConvertToTableEntity() {
            var uriString = new Uri("https://localhost:9090");
            var converter = GetEntityConverter();
            var entity1 = new Entity1(
                            Guid.NewGuid(),
                            "test",
                            DateTimeOffset.UtcNow,
                            123,
                            12.44,
                            TestEnum.TheTwo, TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                            "renamed",
                            new TestObject {
                                TheName = "testobject",
                                TheEnum = TestEnum.TheTwo,
                                TheFlag = TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                                TheEnumValue = TestEnumValue.One
                            },
                            null,
                            uriString,
                            null
                            );
            var tableEntity = converter.ToTableEntity(entity1).Result;

            Assert.NotNull(tableEntity);
            Assert.Equal(entity1.Id.ToString(), tableEntity.PartitionKey);
            Assert.Equal(entity1.TheName.ToString(), tableEntity.RowKey);
            Assert.Equal(entity1.TheDate, tableEntity.GetDateTimeOffset("the_date"));
            Assert.Equal(entity1.TheNumber, tableEntity.GetInt32("the_number"));
            Assert.Equal(entity1.TheFloat, tableEntity.GetDouble("the_float"));
            Assert.Equal("the_two", tableEntity.GetString("the_enum"));
            Assert.Equal("flag_one,flag_two", tableEntity.GetString("the_flag"));
            Assert.Equal("renamed", tableEntity.GetString("a__special__name"));

            Assert.Equal(uriString, new Uri(tableEntity.GetString("test_uri")));


            var json = JsonNode.Parse(tableEntity.GetString("the_object"))?.AsObject() ?? throw new InvalidOperationException("Could not parse objec");

            Assert.True(json.TryGetPropertyValue("the_name", out var theName));
            Assert.True(json.TryGetPropertyValue("the_enum", out var theEnum));
            Assert.True(json.TryGetPropertyValue("the_flag", out var theFlag));
            Assert.True(json.TryGetPropertyValue("the_enum_value", out var theEnumValue));

            Assert.Equal(entity1.TheObject.TheName, theName?.GetValue<string>());
            Assert.Equal("the_two", theEnum?.GetValue<string>());
            Assert.Equal("flag_one,flag_two", theFlag?.GetValue<string>());
            Assert.Equal((int)TestEnumValue.One, theEnumValue?.GetValue<int>());
        }

        [Fact]
        public void TestFromtableEntity() {
            var converter = GetEntityConverter();
            var tableEntity = new TableEntity(Guid.NewGuid().ToString(), "test") {
                {"the_date", DateTimeOffset.UtcNow },
                { "the_number", 1234},
                { "the_float", 12.34},
                { "the_enum", "the_two"},
                { "the_flag", "flag_one,flag_two"},
                { "a__special__name", "renamed"},
                { "the_object", "{\"the_name\": \"testName\", \"the_enum\": \"the_one\", \"the_flag\": \"flag_one,flag_two\"}"},
                { "test_null", null},
            };

            var entity1 = converter.ToRecord<Entity1>(tableEntity);

            Assert.NotNull(entity1);
            Assert.Equal(tableEntity.PartitionKey, entity1.Id.ToString());
            Assert.Equal(tableEntity.RowKey, entity1.TheName.ToString());
            Assert.Equal(tableEntity.GetDateTimeOffset("the_date"), entity1.TheDate);
            Assert.Equal(tableEntity.GetInt32("the_number"), entity1.TheNumber);
            Assert.Equal(tableEntity.GetDouble("the_float"), entity1.TheFloat);
            Assert.Equal(TestEnum.TheTwo, entity1.TheEnum);
            Assert.Equal(tableEntity.GetString("a__special__name"), entity1.Renamed);
            Assert.Null(tableEntity.GetString("test_null"));
            Assert.Null(entity1.TestNull);

            Assert.Equal("testName", entity1.TheObject.TheName);
            Assert.Equal(TestEnum.TheOne, entity1.TheObject.TheEnum);
            Assert.Equal(TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo, entity1.TheObject.TheFlag);

        }

        [Fact]
        public void TestConvertPascalToSnakeCase() {
            var testCases = new[] {
                ("simpleTest", "simple_test"),
                ("easy", "easy"),
                ("HTML", "html"),
                ("simpleXML", "simple_xml"),
                ("PDFLoad", "pdf_load"),
                ("startMIDDLELast", "start_middle_last"),
                ("AString", "a_string"),
                ("Some4Numbers234", "some4_numbers234"),
                ("TEST123String", "test123_string"),
                ("TheTwo", "the_two"),
                ("___Value2", "___value2"),
                ("V_A_L_U_E_3", "v_a_l_u_e_3"),
                ("ALLCAPS", "allcaps"),
            };

            foreach (var (input, expected) in testCases) {
                var actual = CaseConverter.PascalToSnake(input);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TestConvertSnakeToPAscalCase() {
            var testCases = new[] {
                ("simple_test" , "SimpleTest"),
                ("easy" , "Easy"),
                ("html" , "Html"),
                ("simple_xml" , "SimpleXml"),
                ("pdf_load" , "PdfLoad"),
                ("start_middle_last" , "StartMiddleLast"),
                ("a_string" , "AString"),
                ("some4_numbers234" , "Some4Numbers234"),
                ("test123_string" , "Test123String"),
                ("the_two" , "TheTwo")
            };

            foreach (var (input, expected) in testCases) {
                var actual = CaseConverter.SnakeToPascal(input);
                Assert.Equal(expected, actual);
            }
        }



        [Fact]
        public void TestEventSerialization() {
            var scalesetId = ScalesetId.Parse(Guid.NewGuid().ToString());
            var hb = new EventNodeHeartbeat(Guid.NewGuid(), scalesetId, PoolName.Parse("test-Poool"), NodeState.Busy);
            var expectedEvent = new EventMessage(Guid.NewGuid(), EventType.NodeHeartbeat, hb, Guid.NewGuid(), "test", DateTime.UtcNow);
            var serialized = JsonSerializer.Serialize(expectedEvent, EntityConverter.GetJsonSerializerOptions());
            var actualEvent = JsonSerializer.Deserialize<EventMessage>((string)serialized, EntityConverter.GetJsonSerializerOptions());
            Assert.Equal(expectedEvent, actualEvent);
        }


        sealed record Entity2(
            [PartitionKey] int Id,
            [RowKey] string TheName
            ) : EntityBase();

        [Fact]
        public void TestIntKey() {
            var expected = new Entity2(10, "test");
            var converter = GetEntityConverter();
            var tableEntity = converter.ToTableEntity(expected).Result;
            var actual = converter.ToRecord<Entity2>(tableEntity);

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.TheName, actual.TheName);
        }

        private static EntityConverter GetEntityConverter() {
            var converter = new EntityConverter(new TestSecretOperations());
            return converter;
        }

        sealed record Entity3(
            [PartitionKey] int Id,
            [RowKey] string TheName,
            Container Container
        ) : EntityBase();

        [Fact]
        public void TestContainerSerialization() {
            var container = Container.Parse("abc-123");
            var expected = new Entity3(123, "abc", container);
            var converter = GetEntityConverter();

            var tableEntity = converter.ToTableEntity(expected).Result;
            var actual = converter.ToRecord<Entity3>(tableEntity);

            Assert.Equal(expected.Container, actual.Container);
            Assert.Equal(expected.Container.String, tableEntity.GetString("container"));
        }

        [Fact]
        public void TestContainerSerialization2() {
            var entityJson =
@"{
    ""Id"": 123,
    ""TheName"": ""abc"",
    ""Container"": ""abc-123""
}";
            var entity = JsonSerializer.Deserialize<Entity3>(entityJson);

            Assert.Equal(123, entity?.Id);
            Assert.Equal("abc", entity?.TheName);
            Assert.Equal("abc-123", entity?.Container.String);
        }


        sealed record Entity4(
                [RowKey][PartitionKey] int Id,
                string TheName,
                Container Container
            ) : EntityBase();

        [Fact]
        public void TestPartitionKeyIsRowKey() {
            var container = Container.Parse("abc-123");
            var expected = new Entity4(123, "abc", container);
            var converter = GetEntityConverter();

            var tableEntity = converter.ToTableEntity(expected).Result;
            Assert.Equal(expected.Id.ToString(), tableEntity.RowKey);
            Assert.Equal(expected.Id.ToString(), tableEntity.PartitionKey);

            var actual = converter.ToRecord<Entity4>(tableEntity);

            Assert.Equal(expected.Container, actual.Container);
            Assert.Equal(expected.Container.String, tableEntity.GetString("container"));
        }


        sealed record TestEnumObject(TestEnumValue TheEnumValue);

        [Fact]
        public void TestSerializeEnumValue() {
            var expectedObject = new TestEnumObject(
                TheEnumValue: TestEnumValue.One
            );

            var serialized = JsonSerializer.Serialize(expectedObject, EntityConverter.GetJsonSerializerOptions());
            var json = JsonDocument.Parse(serialized);
            Assert.Equal((int)expectedObject.TheEnumValue, json.RootElement.GetProperty("the_enum_value").GetInt32());
            var actual = JsonSerializer.Deserialize<TestEnumObject>(serialized, EntityConverter.GetJsonSerializerOptions());
            Assert.Equal(expectedObject, actual);
        }


        sealed record TestNullField(int? Id, string? Name, TestObject? Obj) : EntityBase();

        [Fact]
        public void TestNullValue() {

            var entityConverter = GetEntityConverter();
            var tableEntity = entityConverter.ToTableEntity(new TestNullField(null, null, null)).Result;

            Assert.Null(tableEntity["id"]);
            Assert.Null(tableEntity["name"]);
            Assert.Null(tableEntity["obj"]);

        }


        [SkipRename]
        enum DoNotRename {
            test1,
            Test_2,
            TEST3
        }


        [Flags]
        [SkipRename]
        enum DoNotRenameFlag {
            test1 = 1 << 0,
            Test_2 = 1 << 1,
            TEST3 = 1 << 2,
        }
        sealed record TestEntity3(DoNotRename Enum, DoNotRenameFlag flag) : EntityBase();


        [Fact]
        public void TestSkipRename() {

            var entityConverter = GetEntityConverter();

            var expected = new TestEntity3(DoNotRename.TEST3, DoNotRenameFlag.Test_2 | DoNotRenameFlag.test1);
            var tableEntity = entityConverter.ToTableEntity(expected).Result;
            Assert.Equal("TEST3", tableEntity.GetString("enum"));
            Assert.Equal("test1,Test_2", tableEntity.GetString("flag"));

            var actual = entityConverter.ToRecord<TestEntity3>(tableEntity);

            Assert.Equal(expected, actual);
        }


        sealed class TestClass {
            public string Name { get; }
            public TestClass() {
                Name = "testName";
            }
        }
        sealed record TestIinit([DefaultValue(InitMethod.DefaultConstructor)] TestClass testClass, string test = "default_test") : EntityBase();

        [Fact]
        public void TestInitValue() {
            var entityConverter = GetEntityConverter();
            var tableEntity = new TableEntity();
            var actual = entityConverter.ToRecord<TestIinit>(tableEntity);

            Assert.Equal("testName", actual.testClass.Name);
            Assert.Equal("default_test", actual.test);
        }


        sealed record TestKeyGetter([PartitionKey] Guid PartitionKey, [RowKey] Guid RowKey);
        [Fact]
        public void TestKeyGetters() {
            var test = new TestKeyGetter(Guid.NewGuid(), Guid.NewGuid());

            var actualPartitionKey = EntityConverter.PartitionKeyGetter<TestKeyGetter>()?.Invoke(test);
            var actualRowKey = EntityConverter.RowKeyGetter<TestKeyGetter>()?.Invoke(test);

            Assert.Equal(test.PartitionKey, actualPartitionKey);
            Assert.Equal(test.RowKey, actualRowKey);
        }

        sealed record NestedEntity(
            [PartitionKey] int Id,
            [RowKey] string TheName,
            [property: TypeDiscrimnator("EventType", typeof(EventTypeProvider))]
            [property: JsonConverter(typeof(BaseEventConverter))]
            Nested? EventType
        ) : EntityBase();

#pragma warning disable CS0169
        public record Nested(
            bool? B,
            Nested? EventType
        ) : BaseEvent();
#pragma warning restore CS0169

        [Fact]
        public void TestDeeplyNestedObjects() {
            var converter = GetEntityConverter();
            var deeplyNestedJson = $"{{{string.Concat(Enumerable.Repeat("\"EventType\": {", 3))}{new String('}', 3)}}}"; // {{{...}}}
            var nestedEntity = new NestedEntity(
                Id: 123,
                TheName: "abc",
                EventType: JsonSerializer.Deserialize<Nested>(deeplyNestedJson, new JsonSerializerOptions())
            );

            var tableEntity = converter.ToTableEntity(nestedEntity).Result;
            var toRecord = () => converter.ToRecord<NestedEntity>(tableEntity);

            _ = toRecord.Should().Throw<Exception>().And.InnerException!.Should().BeOfType<OrmInvalidDiscriminatorFieldException>();
        }

        sealed record TestSecret(
            [PartitionKey] int Id,
            [RowKey] string TheName,
            ISecret<string> MySecret
        ) : EntityBase();

        [Fact]
        public void TestSavingSecret() {
            var converter = GetEntityConverter();
            var test = new TestSecret(1, "test", new SecretValue<string>("blah"));
            var tableEntity = converter.ToTableEntity(test).Result;
            var record = converter.ToRecord<TestSecret>(tableEntity);

            _ = Assert.IsType<SecretAddress<string>>(record.MySecret);
        }


    }
}
