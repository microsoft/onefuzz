using Azure.Data.Tables;
using System;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Azure;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public abstract record EntityBase
{
    public ETag? ETag { get; set; }
    public DateTimeOffset? TimeStamp { get; set; }

}

/// Indicates that the enum cases should no be renamed
[AttributeUsage(AttributeTargets.Enum)]
public class SkipRename : Attribute {  }

public class RowKeyAttribute : Attribute { }
public class PartitionKeyAttribute : Attribute { }
public enum EntityPropertyKind
{
    PartitionKey,
    RowKey,
    Column
}
public record EntityProperty(string name, string columnName, Type type, EntityPropertyKind kind);
public record EntityInfo(Type type, EntityProperty[] properties, Func<object?[], object> constructor);

class OnefuzzNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return CaseConverter.PascalToSnake(name);
    }
}
public class EntityConverter
{
    private readonly JsonSerializerOptions _options;

    private readonly ConcurrentDictionary<Type, EntityInfo> _cache;


    public EntityConverter()
    {
        _options = GetJsonSerializerOptions();
        _cache = new ConcurrentDictionary<Type, EntityInfo>();
    }


    public static JsonSerializerOptions GetJsonSerializerOptions() {
        var options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = new OnefuzzNamingPolicy(),
        };
        options.Converters.Add(new CustomEnumConverterFactory());
        return options;
    }

    internal Func<object?[], object> BuildConstructerFrom(ConstructorInfo constructorInfo)
    {
        var constructorParameters = Expression.Parameter(typeof(object?[]));

        var parameterExpressions =
            constructorInfo.GetParameters().Select((parameterInfo, i) =>
            {
                var ithIndex = Expression.Constant(i);
                var ithParameter = Expression.ArrayIndex(constructorParameters, ithIndex);
                var unboxedIthParameter = Expression.Convert(ithParameter, parameterInfo.ParameterType);
                return unboxedIthParameter;

            }).ToArray();

        NewExpression constructorCall = Expression.New(constructorInfo, parameterExpressions);

        Func<object?[], object> ctor = Expression.Lambda<Func<object?[], object>>(constructorCall, constructorParameters).Compile();
        return ctor;
    }

    private EntityInfo GetEntityInfo<T>()
    {
        return _cache.GetOrAdd(typeof(T), type =>
        {
            var constructor = type.GetConstructors()[0];
            var parameterInfos = constructor.GetParameters();
            var parameters =
            parameterInfos.Select(f =>
            {
                if (f.Name == null)
                {
                    throw new Exception();
                }


                var isRowkey = f.GetCustomAttribute(typeof(RowKeyAttribute)) != null;
                var isPartitionkey = f.GetCustomAttribute(typeof(PartitionKeyAttribute)) != null;


                var (columnName, kind) =
                isRowkey
                    ? ("RowKey", EntityPropertyKind.RowKey)
                    : isPartitionkey
                        ? ("PartitionKey", EntityPropertyKind.PartitionKey)
                        : (// JsonPropertyNameAttribute can only be applied to properties
                            typeof(T).GetProperty(f.Name)?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                                ?? CaseConverter.PascalToSnake(f.Name),
                            EntityPropertyKind.Column
                        );
                

                if (f.ParameterType == null)
                {
                    throw new Exception();
                }

                return new EntityProperty(f.Name, columnName, f.ParameterType, kind);
            }).ToArray();

            return new EntityInfo(typeof(T), parameters, BuildConstructerFrom(constructor));
        });
    }

    public TableEntity ToTableEntity<T>(T typedEntity) where T: EntityBase
    {
        if (typedEntity == null)
        {
            throw new NullReferenceException();
        }
        var type = typeof(T)!;
        if (type is null)
        {
            throw new NullReferenceException();
        }
        var tableEntity = new TableEntity();
        var entityInfo = GetEntityInfo<T>();
        foreach (var prop in entityInfo.properties)
        {
            var value = entityInfo.type.GetProperty(prop.name)?.GetValue(typedEntity);
            if (prop.type == typeof(Guid) || prop.type == typeof(Guid?))
            {
                tableEntity.Add(prop.columnName, value?.ToString());
            }
            else if (prop.type == typeof(bool)
               || prop.type == typeof(bool?)
               || prop.type == typeof(string)
               || prop.type == typeof(DateTime)
               || prop.type == typeof(DateTime?)
               || prop.type == typeof(DateTimeOffset)
               || prop.type == typeof(DateTimeOffset?)
               || prop.type == typeof(int)
               || prop.type == typeof(int?)
               || prop.type == typeof(Int64)
               || prop.type == typeof(Int64?)
               || prop.type == typeof(double)
               || prop.type == typeof(double?)

           )
            {
                tableEntity.Add(prop.columnName, value);
            }
            else if (prop.type.IsEnum)
            {
                var values =
                    (value?.ToString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(CaseConverter.PascalToSnake)).EnsureNotNull($"Unable to read enum data {value}");

                tableEntity.Add(prop.columnName, string.Join(",", values));
            }
            else
            {
                var serialized = JsonSerializer.Serialize(value, _options);
                tableEntity.Add(prop.columnName, serialized);
            }

        }

        if (typedEntity.ETag.HasValue) {
            tableEntity.ETag = typedEntity.ETag.Value;
        }

        return tableEntity;
    }


    public T ToRecord<T>(TableEntity entity) where T: EntityBase
    {
        var entityInfo = GetEntityInfo<T>();
        var parameters =
            entityInfo.properties.Select(ef =>
                {
                    if (ef.kind == EntityPropertyKind.PartitionKey || ef.kind == EntityPropertyKind.RowKey)
                    {
                        if (ef.type == typeof(string))
                            return entity.GetString(ef.kind.ToString());
                        else if (ef.type == typeof(Guid))
                            return Guid.Parse(entity.GetString(ef.kind.ToString()));
                        else
                        {
                            throw new Exception("invalid ");
                        }

                    }

                    var fieldName = ef.columnName;
                    if (ef.type == typeof(string))
                    {
                        return entity.GetString(fieldName);
                    }
                    else if (ef.type == typeof(bool))
                    {
                        return entity.GetBoolean(fieldName);
                    }
                    else if (ef.type == typeof(DateTimeOffset) || ef.type == typeof(DateTimeOffset?))
                    {
                        return entity.GetDateTimeOffset(fieldName);
                    }
                    else if (ef.type == typeof(DateTime))
                    {
                        return entity.GetDateTime(fieldName);
                    }
                    else if (ef.type == typeof(double))
                    {
                        return entity.GetDouble(fieldName);
                    }
                    else if (ef.type == typeof(Guid) || ef.type == typeof(Guid?))
                    {
                        return (object?)Guid.Parse(entity.GetString(fieldName));
                    }
                    else if (ef.type == typeof(int))
                    {
                        return entity.GetInt32(fieldName);
                    }
                    else if (ef.type == typeof(Int64))
                    {
                        return entity.GetInt64(fieldName);
                    }
                    else if (ef.type.IsEnum)
                    {
                        var stringValues =
                            entity.GetString(fieldName).Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(CaseConverter.SnakeToPascal);

                        return Enum.Parse(ef.type, string.Join(",", stringValues));
                    }
                    else
                    {
                        var value = entity.GetString(fieldName);
                        return JsonSerializer.Deserialize(value, ef.type, options: _options); ;
                    }
                }
            ).ToArray();

        var entityRecord = (T)entityInfo.constructor.Invoke(parameters);
        entityRecord.ETag = entity.ETag;
        entityRecord.TimeStamp = entity.Timestamp;

        return entityRecord;
    }

}



