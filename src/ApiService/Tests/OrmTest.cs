using System;
using Xunit;
using Microsoft.OneFuzz.Service;
using Azure.Data.Tables;

namespace Tests
{
    public class OrmTest
    {
        record Entity1([PartitionKey] Guid Id, [RowKey] string TheName, DateTimeOffset TheDate, int TheNumber, double TheFloat);


        [Fact]
        public void TestConvertToTableEntity()
        {
            var converter = new EntityConverter();
            var entity1 = new Entity1(Guid.NewGuid(), "test", DateTimeOffset.UtcNow, 123, 12.44);
            var tableEntity = converter.ToTableEntity(entity1);

            Assert.NotNull(tableEntity);
            Assert.Equal(entity1.Id.ToString(), tableEntity.PartitionKey);
            Assert.Equal(entity1.TheName.ToString(), tableEntity.RowKey);
            Assert.Equal(entity1.TheDate, tableEntity.GetDateTimeOffset("the_date"));
            Assert.Equal(entity1.TheNumber, tableEntity.GetInt32("the_number"));
            Assert.Equal(entity1.TheFloat, tableEntity.GetDouble("the_float"));
        }

        [Fact]
        public void TestFromtableEntity()
        {
            var converter = new EntityConverter();
            var tableEntity = new TableEntity(Guid.NewGuid().ToString(), "test") {
                {"the_date", DateTimeOffset.UtcNow },
                { "the_number", 1234},
                { "the_float", 12.34}
            };

            var entity1 = converter.ToRecord<Entity1>(tableEntity);

            Assert.NotNull(entity1);
            Assert.Equal(tableEntity.PartitionKey, entity1.Id.ToString());
            Assert.Equal(tableEntity.RowKey, entity1.TheName.ToString());
            Assert.Equal(tableEntity.GetDateTimeOffset("the_date"), entity1.TheDate);
            Assert.Equal(tableEntity.GetInt32("the_number"), entity1.TheNumber);
            Assert.Equal(tableEntity.GetDouble("the_float"), entity1.TheFloat);
        }
    }
}