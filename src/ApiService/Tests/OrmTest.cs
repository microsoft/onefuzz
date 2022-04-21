using System;
using Xunit;
using Azure.Data.Tables;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service;
using System.Text.Json;

namespace Tests
{
    public class OrmTest
    {

        class TestObject
        {
            public String? TheName { get; set; }
            public TestEnum TheEnum { get; set; }
            public TestFlagEnum TheFlag { get; set; }
        }

        enum TestEnum
        {
            TheOne,
            TheTwo,
        }

        [Flags]
        enum TestFlagEnum
        {
            FlagOne = 1,
            FlagTwo = 2,
        }

        record Entity1(
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
        public void TestBothDirections()
        {
            var uriString = "https://localhost:9090";
            var converter = new EntityConverter();
            var entity1 = new Entity1(
                            Guid.NewGuid(),
                            "test",
                            DateTimeOffset.UtcNow,
                            123,
                            12.44,
                            TestEnum.TheTwo, TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                            "renamed",
                            new TestObject
                            {
                                TheName = "testobject",
                                TheEnum = TestEnum.TheTwo,
                                TheFlag = TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo
                            },
                            null,
                            new Uri(uriString),
                            null
                            );


            var tableEntity = converter.ToTableEntity(entity1);
            var fromTableEntity = converter.ToRecord<Entity1>(tableEntity);
            var eq = fromTableEntity == entity1;

            Assert.Equal(fromTableEntity.TimeStamp, entity1.TimeStamp);
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
            Assert.Equal(fromTableEntity.TimeStamp, entity1.TimeStamp);

            Assert.Equal(fromTableEntity.TheObject.TheEnum, entity1.TheObject.TheEnum);
            Assert.Equal(fromTableEntity.TheObject.TheFlag, entity1.TheObject.TheFlag);
            Assert.Equal(fromTableEntity.TheObject.TheName, entity1.TheObject.TheName);
        }


        [Fact]
        public void TestConvertToTableEntity()
        {
            var uriString = "https://localhost:9090";
            var converter = new EntityConverter();
            var entity1 = new Entity1(
                            Guid.NewGuid(),
                            "test",
                            DateTimeOffset.UtcNow,
                            123,
                            12.44,
                            TestEnum.TheTwo, TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo,
                            "renamed",
                            new TestObject
                            {
                                TheName = "testobject",
                                TheEnum = TestEnum.TheTwo,
                                TheFlag = TestFlagEnum.FlagOne | TestFlagEnum.FlagTwo
                            },
                            null,
                            new Uri(uriString),
                            null
                            );
            var tableEntity = converter.ToTableEntity(entity1);

            Assert.NotNull(tableEntity);
            Assert.Equal(entity1.Id.ToString(), tableEntity.PartitionKey);
            Assert.Equal(entity1.TheName.ToString(), tableEntity.RowKey);
            Assert.Equal(entity1.TheDate, tableEntity.GetDateTimeOffset("the_date"));
            Assert.Equal(entity1.TheNumber, tableEntity.GetInt32("the_number"));
            Assert.Equal(entity1.TheFloat, tableEntity.GetDouble("the_float"));
            Assert.Equal("the_two", tableEntity.GetString("the_enum"));
            Assert.Equal("flag_one,flag_two", tableEntity.GetString("the_flag"));
            Assert.Equal("renamed", tableEntity.GetString("a__special__name"));

            Assert.Equal(uriString, tableEntity.GetString("test_uri"));


            var json = JsonNode.Parse(tableEntity.GetString("the_object"))?.AsObject() ?? throw new InvalidOperationException("Could not parse objec");

            json.TryGetPropertyValue("the_name", out var theName);
            json.TryGetPropertyValue("the_enum", out var theEnum);
            json.TryGetPropertyValue("the_flag", out var theFlag);

            Assert.Equal(entity1.TheObject.TheName, theName?.GetValue<string>());
            Assert.Equal("the_two", theEnum?.GetValue<string>());
            Assert.Equal("flag_one,flag_two", theFlag?.GetValue<string>());

        }

        [Fact]
        public void TestFromtableEntity()
        {
            var converter = new EntityConverter();
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
        public void TestConvertPascalToSnakeCase()
        {
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

            foreach (var (input, expected) in testCases)
            {
                var actual = CaseConverter.PascalToSnake(input);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TestConvertSnakeToPAscalCase()
        {
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

            foreach (var (input, expected) in testCases)
            {
                var actual = CaseConverter.SnakeToPascal(input);
                Assert.Equal(expected, actual);
            }
        }



        [Fact]
        public void TestEventSerialization()
        {
            var expectedEvent = new EventMessage(Guid.NewGuid(), EventType.NodeHeartbeat, new EventNodeHeartbeat(Guid.NewGuid(), Guid.NewGuid(), "test Poool"), Guid.NewGuid(), "test");
            var serialized = JsonSerializer.Serialize(expectedEvent, EntityConverter.GetJsonSerializerOptions());
            var actualEvent = JsonSerializer.Deserialize<EventMessage>(serialized, EntityConverter.GetJsonSerializerOptions());
            Assert.Equal(expectedEvent, actualEvent);
        }


        record Entity2(
            [PartitionKey] int Id,
            [RowKey] string TheName
            ) : EntityBase();

        [Fact]
        public void TestIntKey()
        {
            var expected = new Entity2(10, "test");
            var converter = new EntityConverter();
            var tableEntity = converter.ToTableEntity(expected);
            var actual = converter.ToRecord<Entity2>(tableEntity);

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.TheName, actual.TheName);
        }


        [Fact]
        public void TestEventSerialization2()
        {

            var converter = new EntityConverter();
            var expectedEvent = new EventMessage(Guid.NewGuid(), EventType.NodeHeartbeat, new EventNodeHeartbeat(Guid.NewGuid(), Guid.NewGuid(), "test Poool"), Guid.NewGuid(), "test")
            {
                ETag = new Azure.ETag("33a64df551425fcc55e4d42a148795d9f25f89d4")
            };
            var te = converter.ToTableEntity(expectedEvent);
            var actualEvent = converter.ToRecord<EventMessage>(te);
            Assert.Equal(expectedEvent, actualEvent);
        }

        record Entity3(
            [PartitionKey] int Id,
            [RowKey] string TheName,
            Container Container
        ) : EntityBase();

        [Fact]
        public void TestContainerSerialization()
        {
            var container = new Container("abc-123");
            var expected = new Entity3(123, "abc", container);
            var converter = new EntityConverter();

            var tableEntity = converter.ToTableEntity(expected);
            var actual = converter.ToRecord<Entity3>(tableEntity);

            Assert.Equal(expected.Container.ContainerName, actual.Container.ContainerName);
            Assert.Equal(expected.Container.ContainerName, tableEntity.GetString("container"));
        }
    }
}
