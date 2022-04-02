using System;
using Xunit;
using Microsoft.OneFuzz.Service;
using Azure.Data.Tables;
using System.Text.Json.Nodes;

namespace Tests
{
    public class OrmTest
    {

        class TestObject { 
            public String TheName { get; set; }
            public TestEnum TheEnum { get; set; }
        }

        enum TestEnum { 
            TheOne,
            TheTwo,
        }

        record Entity1([PartitionKey] Guid Id, [RowKey] string TheName, DateTimeOffset TheDate, int TheNumber, double TheFloat, TestEnum TheEnum, TestObject TheObject);


        [Fact]
        public void TestConvertToTableEntity()
        {
            var converter = new EntityConverter();
            var entity1 = new Entity1(Guid.NewGuid(), "test", DateTimeOffset.UtcNow, 123, 12.44, TestEnum.TheTwo, new TestObject { TheName = "testobject", TheEnum = TestEnum.TheTwo });
            var tableEntity = converter.ToTableEntity(entity1);

            Assert.NotNull(tableEntity);
            Assert.Equal(entity1.Id.ToString(), tableEntity.PartitionKey);
            Assert.Equal(entity1.TheName.ToString(), tableEntity.RowKey);
            Assert.Equal(entity1.TheDate, tableEntity.GetDateTimeOffset("the_date"));
            Assert.Equal(entity1.TheNumber, tableEntity.GetInt32("the_number"));
            Assert.Equal(entity1.TheFloat, tableEntity.GetDouble("the_float"));
            Assert.Equal("the_two", tableEntity.GetString("the_enum"));

            var json = JsonNode.Parse(tableEntity.GetString("the_object"))?.AsObject();
            json.TryGetPropertyValue("the_name", out var theName);
            json.TryGetPropertyValue("the_enum", out var theEnum);

            Assert.Equal(entity1.TheObject.TheName, theName.GetValue<string>() );
            Assert.Equal("the_two", theEnum.GetValue<string>());
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
                { "the_object", "{\"the_name\": \"testName\", \"the_enum\": \"the_one\"}"},
            };

            var entity1 = converter.ToRecord<Entity1>(tableEntity);

            Assert.NotNull(entity1);
            Assert.Equal(tableEntity.PartitionKey, entity1.Id.ToString());
            Assert.Equal(tableEntity.RowKey, entity1.TheName.ToString());
            Assert.Equal(tableEntity.GetDateTimeOffset("the_date"), entity1.TheDate);
            Assert.Equal(tableEntity.GetInt32("the_number"), entity1.TheNumber);
            Assert.Equal(tableEntity.GetDouble("the_float"), entity1.TheFloat);
            Assert.Equal(TestEnum.TheTwo, entity1.TheEnum);

            Assert.Equal("testName", entity1.TheObject.TheName);
            Assert.Equal(TestEnum.TheOne, entity1.TheObject.TheEnum);

        }
    }
}