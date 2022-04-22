using Azure.Data.Tables;
using System.Reflection;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Azure;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public abstract record EntityBase
{
    [JsonIgnore] public ETag? ETag { get; set; }
    public DateTimeOffset? TimeStamp { get; set; }
}

public abstract record StatefulEntityBase<T>([property: JsonIgnore] T state) : EntityBase() where T : Enum;

/// Indicates that the enum cases should no be renamed
[AttributeUsage(AttributeTargets.Enum)]
public class SkipRename : Attribute { }
public class RowKeyAttribute : Attribute { }
public class PartitionKeyAttribute : Attribute { }
public class TypeDiscrimnatorAttribute : Attribute
{
    public string FieldName { get; }
    // the type of a function that takes the value of fieldName as an input and return the type
    public Type ConverterType { get; }

    public TypeDiscrimnatorAttribute(string fieldName, Type converterType)
    {
        if (!converterType.IsAssignableTo(typeof(ITypeProvider)))
        {
            throw new ArgumentException($"the provided type needs to implement ITypeProvider");
        }

        FieldName = fieldName;
        ConverterType = converterType;
    }
}

public interface ITypeProvider
{
    Type GetTypeInfo(object input);
}

public enum EntityPropertyKind
{
    PartitionKey,
    RowKey,
    Column
}
public record EntityProperty(string name, string columnName, Type type, EntityPropertyKind kind, (TypeDiscrimnatorAttribute, ITypeProvider)? discriminator);
public record EntityInfo(Type type, Dictionary<string, EntityProperty> properties, Func<object?[], object> constructor);

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

    private readonly ETag _emptyETag = new ETag();

    public EntityConverter()
    {
        _options = GetJsonSerializerOptions();
        _cache = new ConcurrentDictionary<Type, EntityInfo>();
    }


    public static JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = new OnefuzzNamingPolicy(),
        };
        options.Converters.Add(new CustomEnumConverterFactory());
        options.Converters.Add(new PolymorphicConverterFactory());
        return options;
    }

    internal static Func<object?[], object> BuildConstructerFrom(ConstructorInfo constructorInfo)
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
                var name = f.Name.EnsureNotNull($"Invalid paramter {f}");
                var parameterType = f.ParameterType.EnsureNotNull($"Invalid paramter {f}");
                var isRowkey = f.GetCustomAttribute(typeof(RowKeyAttribute)) != null;
                var isPartitionkey = f.GetCustomAttribute(typeof(PartitionKeyAttribute)) != null;



                var (columnName, kind) =
                isRowkey
                    ? ("RowKey", EntityPropertyKind.RowKey)
                    : isPartitionkey
                        ? ("PartitionKey", EntityPropertyKind.PartitionKey)
                        : (// JsonPropertyNameAttribute can only be applied to properties
                            typeof(T).GetProperty(name)?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                                ?? CaseConverter.PascalToSnake(name),
                            EntityPropertyKind.Column
                        );
                var discriminatorAttribute = type.GetProperty(name)?.GetCustomAttribute<TypeDiscrimnatorAttribute>();

                (TypeDiscrimnatorAttribute, ITypeProvider)? discriminator = null;
                if (discriminatorAttribute != null)
                {
                    var t = (ITypeProvider)(discriminatorAttribute.ConverterType.GetConstructor(new Type[] { })?.Invoke(null) ?? throw new Exception("unable to retrive the type provider"));
                    discriminator = (discriminatorAttribute, t);
                }
                return new EntityProperty(name, columnName, parameterType, kind, discriminator);
            }).ToArray();

            return new EntityInfo(typeof(T), parameters.ToDictionary(x => x.name), BuildConstructerFrom(constructor));
        });
    }

    public string ToJsonString<T>(T typedEntity) where T : EntityBase
    {
        var serialized = JsonSerializer.Serialize(typedEntity, _options);
        return serialized;
    }

    public TableEntity ToTableEntity<T>(T typedEntity) where T : EntityBase
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
        foreach (var kvp in entityInfo.properties)
        {
            var prop = kvp.Value;
            var value = entityInfo.type.GetProperty(prop.name)?.GetValue(typedEntity);
            if (prop.kind == EntityPropertyKind.PartitionKey || prop.kind == EntityPropertyKind.RowKey)
            {
                tableEntity.Add(prop.columnName, value?.ToString());
            }
            else if (prop.type == typeof(Guid) || prop.type == typeof(Guid?))
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
                tableEntity.Add(prop.columnName, serialized.Trim('"'));
            }

        }

        if (typedEntity.ETag.HasValue)
        {
            tableEntity.ETag = typedEntity.ETag.Value;
        }

        return tableEntity;
    }


    private object? GetFieldValue(EntityInfo info, string name, TableEntity entity)
    {
        var ef = info.properties[name];
        if (ef.kind == EntityPropertyKind.PartitionKey || ef.kind == EntityPropertyKind.RowKey)
        {
            if (ef.type == typeof(string))
                return entity.GetString(ef.kind.ToString());
            else if (ef.type == typeof(Guid))
                return Guid.Parse(entity.GetString(ef.kind.ToString()));
            else if (ef.type == typeof(int))
                return int.Parse(entity.GetString(ef.kind.ToString()));
            else
            {
                throw new Exception("invalid ");
            }
        }

        var fieldName = ef.columnName;
        var obj = entity[fieldName];
        if (obj == null)
        {
            return null;
        }
        var objType = obj.GetType();

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
            var outputType = ef.type;
            if (ef.discriminator != null)
            {
                var (attr, typeProvider) = ef.discriminator.Value;
                var v = GetFieldValue(info, attr.FieldName, entity) ?? throw new Exception($"No value for {attr.FieldName}");
                outputType = typeProvider.GetTypeInfo(v);
            }


            if (objType == typeof(string))
            {
                var value = entity.GetString(fieldName);
                if (value.StartsWith('[') || value.StartsWith('{') || value == "null")
                {
                    return JsonSerializer.Deserialize(value, outputType, options: _options);
                }
                else
                {
                    return JsonSerializer.Deserialize($"\"{value}\"", outputType, options: _options);
                }
            }
            else
            {
                var value = entity.GetString(fieldName);
                return JsonSerializer.Deserialize(value, outputType, options: _options);
            }
        }
    }


    public T ToRecord<T>(TableEntity entity) where T : EntityBase
    {
        var entityInfo = GetEntityInfo<T>();
        var parameters =
            entityInfo.properties.Keys.Select(k => GetFieldValue(entityInfo, k, entity)).ToArray();

        var entityRecord = (T)entityInfo.constructor.Invoke(parameters);

        if (entity.ETag != _emptyETag)
        {
            entityRecord.ETag = entity.ETag;
        }
        entityRecord.TimeStamp = entity.Timestamp;

        return entityRecord;
    }

}



