using Azure.Data.Tables;
using CaseExtensions;
using System;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;

namespace Microsoft.OneFuzz.Service;


public class RowKeyAttribute : Attribute { }
public class PartitionKeyAttribute : Attribute { }
public enum EntityPropertyKind
{
    PartitionKey,
    RowKey,
    Column
}
public record EntityProperty(string name, string dbName, Type type, EntityPropertyKind kind);
public record EntityInfo(Type type, EntityProperty[] properties, Func<object[], object> constructor);

public class EntityConverter
{

    internal Func<object[], object> BuildConstructerFrom(ConstructorInfo constructorInfo)
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

        Func<object[], object> ctor = (Func<object[], object>)Expression.Lambda<Func<object[], object>>(constructorCall, constructorParameters).Compile();
        return ctor;
    }

    private EntityInfo GetEntityInfo<T>()
    {
        var constructor = typeof(T).GetConstructors()[0];
        //Delegate.CreateDelegate(typeof(Func<object[], object>), null, constructor.method);

        var parameterInfos = constructor.GetParameters();
        var parameters =
        parameterInfos.Select(f =>
            {
                var isRowkey = f.GetCustomAttribute(typeof(RowKeyAttribute)) != null;
                var isPartitionkey = f.GetCustomAttribute(typeof(PartitionKeyAttribute)) != null;
                var (dbName, kind) = isRowkey ? ("RowKey", EntityPropertyKind.RowKey) : isPartitionkey ? ("PartitionKey", EntityPropertyKind.PartitionKey) : (f.Name.ToSnakeCase(), EntityPropertyKind.Column);
                Func<object, object> parser;
                if (isRowkey || isPartitionkey)
                {
                    if (f.ParameterType == typeof(string))
                    {
                        parser = (x) => x;
                    }
                    else
                    {
                        var parserFunc = f.ParameterType?.GetMethod("Parse", new[] { typeof(string) });
                        //var serializerFunc = f.ParameterType?.GetMethod("ToString", new Type[] {});
                        if (parserFunc == null)
                        {
                            throw new NullReferenceException($"no parser for type {f.ParameterType}");
                        }
                        //parser = (Func<object,object>) Delegate.CreateDelegate(typeof(Func<object,object>),null,parserFunc);
                    }
                }
                else
                {
                    //todo:
                    parser = x => x;
                }
                if (f.Name == null)
                {
                    throw new Exception();
                }

                if (f.ParameterType == null)
                {
                    throw new Exception();
                }

                return new EntityProperty(f.Name, dbName, f.ParameterType, kind);
            }).ToArray();

        return new EntityInfo(typeof(T), parameters, BuildConstructerFrom(constructor));
    }

    public TableEntity ToTableEntity<T>(T typedEntity)
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
                tableEntity.Add(prop.dbName, value?.ToString());
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
                tableEntity.Add(prop.dbName, value);
            }
            else
            {
                tableEntity.Add(prop.dbName, System.Text.Json.JsonSerializer.Serialize(value));
            }

        }

        return tableEntity;
    }


    public T ToRecord<T>(TableEntity entity)
    {
        var entityInfo = GetEntityInfo<T>();
        var parameters =
        entityInfo.properties.Select(ef =>
            {
                if (ef.kind == EntityPropertyKind.PartitionKey || ef.kind == EntityPropertyKind.RowKey)
                {
                    if (ef.type == typeof(string))
                        return (object)entity.GetString(ef.kind.ToString());
                    else if (ef.type == typeof(Guid))
                        return (object)Guid.Parse(entity.GetString(ef.kind.ToString()));
                    else
                    {
                        throw new Exception("invalid ");
                    }

                }

                var fieldName = ef.dbName;
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
                else
                {
                    // todo: deserialize the object using system.text.json
                    // we expect a string at this point
                    var value = entity.GetString(fieldName);
                    System.Text.Json.JsonSerializer.Deserialize(value, ef.type);
                    return System.Text.Json.JsonSerializer.Deserialize(value, ef.type); ;
                }
            }
        ).ToArray();



        return (T)entityInfo.constructor.Invoke(parameters);
    }
}
