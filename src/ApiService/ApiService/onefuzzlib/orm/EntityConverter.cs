﻿using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public abstract record EntityBase {
    [JsonIgnore] public ETag? ETag { get; set; }
    public DateTimeOffset? TimeStamp { get; set; }

    // https://docs.microsoft.com/en-us/rest/api/storageservices/designing-a-scalable-partitioning-strategy-for-azure-table-storage#yyy
    // Produce "good-quality-table-key" based on a DateTimeOffset timestamp
    public static string NewSortedKey => $"{DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UtcNow.Ticks}";
}

public abstract record StatefulEntityBase<T>([property: JsonIgnore] T State) : EntityBase() where T : Enum;



/// How the value is populated
public enum InitMethod {
    //T() will be used
    DefaultContructor,
}
[AttributeUsage(AttributeTargets.Parameter)]
public class DefaultValueAttribute : Attribute {

    public InitMethod InitMethod { get; }
    public DefaultValueAttribute(InitMethod initMethod) {
        InitMethod = initMethod;
    }
}

/// Indicates that the enum cases should no be renamed
[AttributeUsage(AttributeTargets.Enum)]
public class SerializeValueAttribute : Attribute { }

/// Indicates that the enum cases should no be renamed
[AttributeUsage(AttributeTargets.Enum)]
public class SkipRenameAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Parameter)]
public class RowKeyAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Parameter)]
public class PartitionKeyAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Property)]
public class TypeDiscrimnatorAttribute : Attribute {
    public string FieldName { get; }
    // the type of a function that takes the value of fieldName as an input and return the type
    public Type ConverterType { get; }

    public TypeDiscrimnatorAttribute(string fieldName, Type converterType) {
        if (!converterType.IsAssignableTo(typeof(ITypeProvider))) {
            throw new ArgumentException($"the provided type needs to implement ITypeProvider");
        }

        FieldName = fieldName;
        ConverterType = converterType;
    }
}

public interface ITypeProvider {
    Type GetTypeInfo(object input);
}

public enum EntityPropertyKind {
    PartitionKey,
    RowKey,
    Column
}
public record EntityProperty(
        string name,
        string columnName,
        Type type,
        EntityPropertyKind kind,
        (TypeDiscrimnatorAttribute, ITypeProvider)? discriminator,
        DefaultValueAttribute? defaultValue,
        ParameterInfo parameterInfo
    );
public record EntityInfo(Type type, ILookup<string, EntityProperty> properties, Func<object?[], object> constructor);

class OnefuzzNamingPolicy : JsonNamingPolicy {
    public override string ConvertName(string name) {
        return CaseConverter.PascalToSnake(name);
    }
}
public class EntityConverter {
    private readonly JsonSerializerOptions _options;

    private readonly ConcurrentDictionary<Type, EntityInfo> _cache;

    public EntityConverter() {
        _options = GetJsonSerializerOptions();
        _cache = new ConcurrentDictionary<Type, EntityInfo>();
    }

    public static JsonSerializerOptions GetJsonSerializerOptions() {
        var options = new JsonSerializerOptions() {
            PropertyNamingPolicy = new OnefuzzNamingPolicy(),
        };
        options.Converters.Add(new CustomEnumConverterFactory());
        options.Converters.Add(new PolymorphicConverterFactory());
        return options;
    }

    internal static Func<object?[], object> BuildConstructerFrom(ConstructorInfo constructorInfo) {
        var constructorParameters = Expression.Parameter(typeof(object?[]));

        var parameterExpressions =
            constructorInfo.GetParameters().Select((parameterInfo, i) => {
                var ithIndex = Expression.Constant(i);
                var ithParameter = Expression.ArrayIndex(constructorParameters, ithIndex);
                var unboxedIthParameter = Expression.Convert(ithParameter, parameterInfo.ParameterType);
                return unboxedIthParameter;

            }).ToArray();

        NewExpression constructorCall = Expression.New(constructorInfo, parameterExpressions);

        Func<object?[], object> ctor = Expression.Lambda<Func<object?[], object>>(constructorCall, constructorParameters).Compile();
        return ctor;
    }

    private static IEnumerable<EntityProperty> GetEntityProperties<T>(ParameterInfo parameterInfo) {
        var name = parameterInfo.Name.EnsureNotNull($"Invalid paramter {parameterInfo}");
        var parameterType = parameterInfo.ParameterType.EnsureNotNull($"Invalid paramter {parameterInfo}");
        var isRowkey = parameterInfo.GetCustomAttribute(typeof(RowKeyAttribute)) != null;
        var isPartitionkey = parameterInfo.GetCustomAttribute(typeof(PartitionKeyAttribute)) != null;

        var discriminatorAttribute = typeof(T).GetProperty(name)?.GetCustomAttribute<TypeDiscrimnatorAttribute>();
        var defaultValueAttribute = parameterInfo.GetCustomAttribute<DefaultValueAttribute>();


        (TypeDiscrimnatorAttribute, ITypeProvider)? discriminator = null;
        if (discriminatorAttribute != null) {
            var t = (ITypeProvider)(Activator.CreateInstance(discriminatorAttribute.ConverterType) ?? throw new Exception("unable to retrive the type provider"));
            discriminator = (discriminatorAttribute, t);
        }


        if (isPartitionkey) {
            yield return new EntityProperty(name, "PartitionKey", parameterType, EntityPropertyKind.PartitionKey, discriminator, defaultValueAttribute, parameterInfo);
        }

        if (isRowkey) {
            yield return new EntityProperty(name, "RowKey", parameterType, EntityPropertyKind.RowKey, discriminator, defaultValueAttribute, parameterInfo);
        }

        if (!isPartitionkey && !isRowkey) {
            var columnName = typeof(T).GetProperty(name)?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? CaseConverter.PascalToSnake(name);
            yield return new EntityProperty(name, columnName, parameterType, EntityPropertyKind.Column, discriminator, defaultValueAttribute, parameterInfo);
        }
    }


    private EntityInfo GetEntityInfo<T>() {
        return _cache.GetOrAdd(typeof(T), type => {
            var constructor = type.GetConstructors()[0];
            var parameterInfos = constructor.GetParameters();
            var parameters =
            parameterInfos.SelectMany(GetEntityProperties<T>).ToArray();

            return new EntityInfo(typeof(T), parameters.ToLookup(x => x.name), BuildConstructerFrom(constructor));
        });
    }

    public string ToJsonString<T>(T typedEntity) => JsonSerializer.Serialize(typedEntity, _options);

    public T? FromJsonString<T>(string value) => JsonSerializer.Deserialize<T>(value, _options);

    public TableEntity ToTableEntity<T>(T typedEntity) where T : EntityBase {
        if (typedEntity == null) {
            throw new ArgumentNullException(nameof(typedEntity));
        }

        var type = typeof(T);

        var entityInfo = GetEntityInfo<T>();
        Dictionary<string, object?> columnValues = entityInfo.properties.SelectMany(x => x).Select(prop => {
            var value = entityInfo.type.GetProperty(prop.name)?.GetValue(typedEntity);
            if (value == null) {
                return (prop.columnName, value: (object?)null);
            }
            if (prop.kind == EntityPropertyKind.PartitionKey || prop.kind == EntityPropertyKind.RowKey) {
                return (prop.columnName, value?.ToString());
            }
            if (prop.type == typeof(Guid) || prop.type == typeof(Guid?)) {
                return (prop.columnName, value?.ToString());
            }
            if (prop.type == typeof(bool)
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

             ) {
                return (prop.columnName, value);
            }

            var serialized = JsonSerializer.Serialize(value, _options);
            return (prop.columnName, serialized.Trim('"'));

        }).ToDictionary(x => x.columnName, x => x.value);

        var tableEntity = new TableEntity(columnValues);

        if (typedEntity.ETag.HasValue) {
            tableEntity.ETag = typedEntity.ETag.Value;
        }

        return tableEntity;
    }


    private object? GetFieldValue(EntityInfo info, string name, TableEntity entity) {
        var ef = info.properties[name].First();
        if (ef.kind == EntityPropertyKind.PartitionKey || ef.kind == EntityPropertyKind.RowKey) {
            if (ef.type == typeof(string))
                return entity.GetString(ef.kind.ToString());
            else if (ef.type == typeof(Guid))
                return Guid.Parse(entity.GetString(ef.kind.ToString()));
            else if (ef.type == typeof(int))
                return int.Parse(entity.GetString(ef.kind.ToString()));
            else if (ef.type == typeof(PoolName))
                // TODO: this should be able to be generic over any ValidatedString
                return PoolName.Parse(entity.GetString(ef.kind.ToString()));
            else {
                throw new Exception($"invalid partition or row key type of {info.type} property {name}: {ef.type}");
            }
        }

        var fieldName = ef.columnName;
        var obj = entity[fieldName];
        if (obj == null) {

            if (ef.parameterInfo.HasDefaultValue) {
                return ef.parameterInfo.DefaultValue;
            }

            return ef.defaultValue switch {
                DefaultValueAttribute { InitMethod: InitMethod.DefaultContructor } => Activator.CreateInstance(ef.type),
                _ => null,
            };
        }
        var objType = obj.GetType();

        if (ef.type == typeof(string)) {
            return entity.GetString(fieldName);
        } else if (ef.type == typeof(bool) || ef.type == typeof(bool?)) {
            return entity.GetBoolean(fieldName);
        } else if (ef.type == typeof(DateTimeOffset) || ef.type == typeof(DateTimeOffset?)) {
            return entity.GetDateTimeOffset(fieldName);
        } else if (ef.type == typeof(DateTime) || ef.type == typeof(DateTime?)) {
            return entity.GetDateTime(fieldName);
        } else if (ef.type == typeof(double) || ef.type == typeof(double?)) {
            return entity.GetDouble(fieldName);
        } else if (ef.type == typeof(Guid) || ef.type == typeof(Guid?)) {
            return (object?)Guid.Parse(entity.GetString(fieldName));
        } else if (ef.type == typeof(int) || ef.type == typeof(short) || ef.type == typeof(int?) || ef.type == typeof(short?)) {
            return entity.GetInt32(fieldName);
        } else if (ef.type == typeof(long) || ef.type == typeof(long?)) {
            return entity.GetInt64(fieldName);
        } else {
            var outputType = ef.type;
            if (ef.discriminator != null) {
                var (attr, typeProvider) = ef.discriminator.Value;
                var v = GetFieldValue(info, attr.FieldName, entity) ?? throw new Exception($"No value for {attr.FieldName}");
                outputType = typeProvider.GetTypeInfo(v);
            }

            if (objType == typeof(string)) {
                var value = entity.GetString(fieldName);
                if (value.StartsWith('[') || value.StartsWith('{') || value == "null") {
                    return JsonSerializer.Deserialize(value, outputType, options: _options);
                } else {
                    return JsonSerializer.Deserialize($"\"{value}\"", outputType, options: _options);
                }
            } else {
                var value = entity.GetString(fieldName);
                return JsonSerializer.Deserialize(value, outputType, options: _options);
            }
        }
    }


    public T ToRecord<T>(TableEntity entity) where T : EntityBase {
        var entityInfo = GetEntityInfo<T>();
        var parameters =
            entityInfo.properties.Select(grouping => GetFieldValue(entityInfo, grouping.Key, entity)).ToArray();
        try {
            var entityRecord = (T)entityInfo.constructor.Invoke(parameters);
            if (entity.ETag != default) {
                entityRecord.ETag = entity.ETag;
            }
            entityRecord.TimeStamp = entity.Timestamp;
            return entityRecord;

        } catch (Exception ex) {
            var stringParam = string.Join(", ", parameters);
            throw new Exception($"Could not initialize object of type {typeof(T)} with the following parameters: {stringParam} constructor {entityInfo.constructor} : {ex}");
        }

    }

}
