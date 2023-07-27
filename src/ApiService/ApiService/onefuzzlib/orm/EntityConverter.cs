using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public abstract record EntityBase {
    [JsonIgnore]
    public ETag? ETag { get; set; }

    [JsonIgnore]
    public DateTimeOffset? Timestamp { get; set; }

    // https://docs.microsoft.com/en-us/rest/api/storageservices/designing-a-scalable-partitioning-strategy-for-azure-table-storage#yyy
    // Produce "good-quality-table-key" based on a DateTimeOffset timestamp
    public static string NewSortedKey => $"{DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UtcNow.Ticks}";
}

public abstract record StatefulEntityBase<T>(T State) : EntityBase() where T : Enum;


/// How the value is populated
public enum InitMethod {
    //T() will be used
    DefaultConstructor,
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

sealed class OnefuzzNamingPolicy : JsonNamingPolicy {
    public override string ConvertName(string name) {
        return CaseConverter.PascalToSnake(name);
    }
}
public class EntityConverter {
    private readonly ISecretsOperations _secretsOperations;

    private const int MAX_DESERIALIZATION_RECURSION_DEPTH = 100;
    private readonly ConcurrentDictionary<Type, EntityInfo> _cache;
    private static readonly JsonSerializerOptions _options = new() {
        PropertyNamingPolicy = new OnefuzzNamingPolicy(),
        Converters = {
            new CustomEnumConverterFactory(),
            new PolymorphicConverterFactory(),
        }
    };

    public EntityConverter(ISecretsOperations secretsOperations) {
        _secretsOperations = secretsOperations;
        _cache = new ConcurrentDictionary<Type, EntityInfo>();
    }

    public static JsonSerializerOptions GetJsonSerializerOptions() {
        return _options;
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
        var name = parameterInfo.Name.EnsureNotNull($"Invalid parameter {parameterInfo}");
        var parameterType = parameterInfo.ParameterType.EnsureNotNull($"Invalid parameter {parameterInfo}");
        var isRowkey = parameterInfo.GetCustomAttribute(typeof(RowKeyAttribute)) != null;
        var isPartitionkey = parameterInfo.GetCustomAttribute(typeof(PartitionKeyAttribute)) != null;

        var discriminatorAttribute = typeof(T).GetProperty(name)?.GetCustomAttribute<TypeDiscrimnatorAttribute>();
        var defaultValueAttribute = parameterInfo.GetCustomAttribute<DefaultValueAttribute>();


        (TypeDiscrimnatorAttribute, ITypeProvider)? discriminator = null;
        if (discriminatorAttribute != null) {
            var t = (ITypeProvider)(Activator.CreateInstance(discriminatorAttribute.ConverterType) ?? throw new Exception("unable to retrieve the type provider"));
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
            var parameters = parameterInfos.SelectMany(GetEntityProperties<T>).ToArray();

            return new EntityInfo(typeof(T), parameters.ToLookup(x => x.name), BuildConstructerFrom(constructor));
        });
    }

    public static string ToJsonString<T>(T typedEntity) => JsonSerializer.Serialize(typedEntity, _options);

    public static T? FromJsonString<T>(string value) => JsonSerializer.Deserialize<T>(value, _options);

    private async ValueTask<(string, object?)> PropertyToColumnValue(EntityProperty prop, object? value) {
        if (value == null) {
            return (prop.columnName, null);
        }

        if (prop.kind == EntityPropertyKind.PartitionKey || prop.kind == EntityPropertyKind.RowKey) {
            return (prop.columnName, value?.ToString());
        }

        if (prop.type == typeof(Guid) || prop.type == typeof(Guid?) || prop.type == typeof(Uri)) {
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
             || prop.type == typeof(long)
             || prop.type == typeof(long?)
             || prop.type == typeof(double)
             || prop.type == typeof(double?)) {
            return (prop.columnName, value);
        }

        // if prop.type is a SecretData
        if (typeof(ISecret).IsAssignableFrom(prop.type)) {
            var secret = (ISecret)value;
            if (!secret.IsHIddden) {
                var kv = await _secretsOperations.StoreSecret(secret);
                value = new SecretAddress<object>(kv);
            }
        }

        var serialized = JsonSerializer.Serialize(value, _options);
        return (prop.columnName, serialized.Trim('"'));
    }

    public async Async.Task<TableEntity> ToTableEntity<T>(T typedEntity) where T : EntityBase {
        if (typedEntity == null) {
            throw new ArgumentNullException(nameof(typedEntity));
        }

        var type = typeof(T);

        var entityInfo = GetEntityInfo<T>();

        var columnValues = new Dictionary<string, object?>();
        foreach (var prop in entityInfo.properties.SelectMany(x => x)) {
            var value = entityInfo.type.GetProperty(prop.name)?.GetValue(typedEntity);
            var (columnName, columnValue) = await PropertyToColumnValue(prop, value);
            columnValues.Add(columnName, columnValue);
        }

        var tableEntity = new TableEntity(columnValues);
        if (typedEntity.ETag.HasValue) {
            tableEntity.ETag = typedEntity.ETag.Value;
        }

        return tableEntity;
    }


    private object? GetFieldValue(EntityInfo info, string name, TableEntity entity, int iterationCount) {
        var ef = info.properties[name].First();
        if (ef.kind == EntityPropertyKind.PartitionKey || ef.kind == EntityPropertyKind.RowKey) {
            // partition & row keys must always be strings
            var stringValue = entity.GetString(ef.kind.ToString());
            if (ef.type == typeof(string)) {
                return stringValue;
            } else if (ef.type == typeof(Guid)) {
                return Guid.Parse(stringValue);
            } else if (ef.type == typeof(int)) {
                return int.Parse(stringValue);
            } else if (ef.type == typeof(long)) {
                return long.Parse(stringValue);
            } else if (ef.type.IsEnum) {
                return Enum.Parse(ef.type, stringValue);
            } else if (ef.type.IsClass) {
                try {
                    if (ef.type.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public) is MethodInfo mi) {
                        return mi.Invoke(null, new[] { stringValue });
                    }
                } catch (Exception ex) {
                    throw new ArgumentException($"Unable to parse '{stringValue}' as {ef.type}", ex);
                }

                return Activator.CreateInstance(ef.type, new[] { stringValue });
            } else {
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
                DefaultValueAttribute { InitMethod: InitMethod.DefaultConstructor } => Activator.CreateInstance(ef.type),
                _ => null,
            };
        }

        try {
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
                    if (iterationCount > MAX_DESERIALIZATION_RECURSION_DEPTH) {
                        var tags = GenerateTableEntityTags(entity);
                        tags.AddRange(new (string, string)[] {
                            ("outputType", outputType?.Name ?? string.Empty),
                            ("fieldName", fieldName)
                        });
                        throw new OrmMaxRecursionDepthReachedException($"MAX_DESERIALIZATION_RECURSION_DEPTH reached. Too many iterations deserializing {info.type}. {PrintTags(tags)}");
                    }
                    if (attr.FieldName == name) {
                        var tags = GenerateTableEntityTags(entity);
                        tags.AddRange(new (string, string)[] {
                            ("outputType", outputType?.Name ?? string.Empty),
                            ("fieldName", fieldName)
                        });
                        throw new OrmInvalidDiscriminatorFieldException($"Discriminator field is the same as the field being deserialized {name}. {PrintTags(tags)}");
                    }
                    var v = GetFieldValue(info, attr.FieldName, entity, ++iterationCount) ?? throw new Exception($"No value for {attr.FieldName}");
                    outputType = typeProvider.GetTypeInfo(v);
                }

                var objType = obj.GetType();
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
        } catch (Exception ex)
            when (ex is not OrmException) {
            var tags = GenerateTableEntityTags(entity);
            tags.AddRange(new (string, string)[] {
                ("fieldName", fieldName)
            });
            throw new InvalidOperationException($"Unable to get value for property '{name}' (entity field '{fieldName}'). {PrintTags(tags)}", ex);
        }
    }


    public T ToRecord<T>(TableEntity entity) where T : EntityBase {
        var entityInfo = GetEntityInfo<T>();

        object?[] parameters;
        try {
            parameters = entityInfo.properties.Select(grouping => GetFieldValue(entityInfo, grouping.Key, entity, 0)).ToArray();
        } catch (Exception ex) {
            throw new InvalidOperationException($"Unable to extract properties from TableEntity for {typeof(T)}", ex);
        }

        try {
            var entityRecord = (T)entityInfo.constructor.Invoke(parameters);
            if (entity.ETag != default) {
                entityRecord.ETag = entity.ETag;
            }

            entityRecord.Timestamp = entity.Timestamp;
            return entityRecord;

        } catch (Exception ex) {
            var stringParam = string.Join(", ", parameters);
            throw new InvalidOperationException($"Could not initialize object of type {typeof(T)} with the following parameters: {stringParam} constructor {entityInfo.constructor}", ex);
        }
    }

    public static Func<T, object?>? PartitionKeyGetter<T>() {
        return
            typeof(T).GetConstructors()
                     .SelectMany(x => x.GetParameters())
                     .FirstOrDefault(p => p.GetCustomAttribute<PartitionKeyAttribute>() != null) switch {
                         null => null, { Name: null } => null, { Name: var name } => BuildGetter<T>(typeof(T).GetProperty(name))
                     };
    }

    public static Func<T, object?>? RowKeyGetter<T>() {
        var x =
            typeof(T).GetConstructors()
                     .SelectMany(x => x.GetParameters())
                     .FirstOrDefault(p => p.GetCustomAttribute<RowKeyAttribute>() != null) switch {
                         null => null, { Name: null } => null, { Name: var name } => BuildGetter<T>(typeof(T).GetProperty(name))
                     };

        return x;
    }

    static Func<T, object?>? BuildGetter<T>(PropertyInfo? property) {
        if (property == null)
            return null;

        var paramter = Expression.Parameter(typeof(T));
        var call = Expression.Convert(Expression.Property(paramter, property), typeof(object));
        return Expression.Lambda<Func<T, object?>>(call, paramter).Compile();
    }

    private static List<(string, string)> GenerateTableEntityTags(TableEntity entity) {
        var entityKeys = string.Join(',', entity.Keys);
        var partitionKey = entity.ContainsKey(EntityPropertyKind.PartitionKey.ToString()) ? entity.GetString(EntityPropertyKind.PartitionKey.ToString()) : string.Empty;
        var rowKey = entity.ContainsKey(EntityPropertyKind.RowKey.ToString()) ? entity.GetString(EntityPropertyKind.RowKey.ToString()) : string.Empty;

        return new List<(string, string)> {
            ("entityKeys", entityKeys),
            ("partitionKey", partitionKey),
            ("rowKey", rowKey)
        };
    }

    private static string PrintTags(List<(string, string)>? tags) {
        return tags != null ? string.Join(", ", tags.Select(x => $"{x.Item1}={x.Item2}")) : string.Empty;
    }
}

public class OrmInvalidDiscriminatorFieldException : OrmException {
    public OrmInvalidDiscriminatorFieldException(string message) : base(message) { }
}

public class OrmMaxRecursionDepthReachedException : OrmException {
    public OrmMaxRecursionDepthReachedException(string message) : base(message) { }
}

public class OrmException : Exception {
    public OrmException(string message) : base(message) { }
}
