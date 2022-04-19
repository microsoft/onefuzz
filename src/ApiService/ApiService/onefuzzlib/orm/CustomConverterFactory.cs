﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public sealed class CustomEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        object[]? knownValues = null;

        if (typeToConvert == typeof(BindingFlags))
        {
            knownValues = new object[] { BindingFlags.CreateInstance | BindingFlags.DeclaredOnly };
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(CustomEnumConverter<>).MakeGenericType(typeToConvert),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            args: new object?[] { options.PropertyNamingPolicy, options, knownValues },
            culture: null)!;
    }
}

public sealed class CustomEnumConverter<T> : JsonConverter<T> where T : Enum
{
    private readonly JsonNamingPolicy _namingPolicy;

    private readonly Dictionary<string, T> _readCache = new();
    private readonly Dictionary<T, JsonEncodedText> _writeCache = new();

    // This converter will only support up to 64 enum values (including flags) on serialization and deserialization
    private const int NameCacheLimit = 64;

    private const string ValueSeparator = ",";

    public CustomEnumConverter(JsonNamingPolicy namingPolicy, JsonSerializerOptions options, object[]? knownValues)
    {
        _namingPolicy = namingPolicy;

        bool continueProcessing = true;
        for (int i = 0; i < knownValues?.Length; i++)
        {
            if (!TryProcessValue((T)knownValues[i]))
            {
                continueProcessing = false;
                break;
            }
        }

        var type = typeof(T);
        var skipFormat = type.GetCustomAttribute<SkipRename>() != null;
        if (continueProcessing)
        {
            Array values = Enum.GetValues(type);

            for (int i = 0; i < values.Length; i++)
            {
                T value = (T)values.GetValue(i)!;

                if (!TryProcessValue(value, skipFormat))
                {
                    break;
                }
            }
        }

        bool TryProcessValue(T value, bool skipFormat = false)
        {
            if (_readCache.Count == NameCacheLimit)
            {
                Debug.Assert(_writeCache.Count == NameCacheLimit);
                return false;
            }

            FormatAndAddToCaches(value, options.Encoder, skipFormat);
            return true;
        }
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? json;

        if (reader.TokenType != JsonTokenType.String || (json = reader.GetString()) == null)
        {
            throw new JsonException();
        }

        var value = json.Split(ValueSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x =>
            {
                if (!_readCache.TryGetValue(x, out T? value))
                {
                    throw new JsonException();
                }
                return value;

            }).ToArray();

        if (value.Length == 1)
        {
            return value[0];
        }

        return (T)(object)value.Aggregate(0, (state, value) => (int)(object)state | (int)(object)value);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!_writeCache.TryGetValue(value, out JsonEncodedText formatted))
        {
            if (_writeCache.Count == NameCacheLimit)
            {
                Debug.Assert(_readCache.Count == NameCacheLimit);
                throw new ArgumentOutOfRangeException();
            }

            formatted = FormatAndAddToCaches(value, options.Encoder);
        }

        writer.WriteStringValue(formatted);
    }

    private JsonEncodedText FormatAndAddToCaches(T value, JavaScriptEncoder? encoder, bool skipFormat = false)
    {
        (string valueFormattedToStr, JsonEncodedText valueEncoded) = FormatEnumValue(value.ToString(), _namingPolicy, encoder, skipFormat);
        _readCache[valueFormattedToStr] = value;
        _writeCache[value] = valueEncoded;
        return valueEncoded;
    }

    private ValueTuple<string, JsonEncodedText> FormatEnumValue(string value, JsonNamingPolicy namingPolicy, JavaScriptEncoder? encoder, bool skipFormat = false)
    {
        string converted;

        if (!value.Contains(ValueSeparator))
        {
            converted = skipFormat ? value : namingPolicy.ConvertName(value);
        }
        else
        {
            // todo: optimize implementation here by leveraging https://github.com/dotnet/runtime/issues/934.
            string[] enumValues = value.Split(ValueSeparator);

            for (int i = 0; i < enumValues.Length; i++)
            {
                var trimmed = enumValues[i].Trim();
                enumValues[i] = skipFormat ? trimmed : namingPolicy.ConvertName(trimmed);
            }

            converted = string.Join(ValueSeparator, enumValues);
        }

        return (converted, JsonEncodedText.Encode(converted, encoder));
    }
}


public sealed class PolymorphicConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var converter = typeToConvert.GetCustomAttribute<JsonConverterAttribute>();
        if (converter != null)
        {
            return false;
        }

        var propertyAndAttributes =
            typeToConvert.GetProperties()
                .Select(p => new { property = p, attribute = p.GetCustomAttribute<TypeDiscrimnatorAttribute>() })
                .Where(p => p.attribute != null)
                .ToList();

        if (propertyAndAttributes.Count == 0)
        {
            return false;
        }

        if (propertyAndAttributes.Count == 1)
        {
            return true;
        }

        else
        {
            throw new InvalidOperationException("the attribute TypeDiscrimnatorAttribute can only be aplied once");
        }
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var (field, attribute) = typeToConvert.GetProperties()
                .Select(p => (p.Name, p.GetCustomAttribute<TypeDiscrimnatorAttribute>()))
                .Where(p => p.Item2 != null)
                .First();


        return (JsonConverter)Activator.CreateInstance(
            typeof(PolymorphicConverter<>).MakeGenericType(typeToConvert),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            args: new object?[] { attribute, field },
            culture: null)!;
    }
}

public sealed class PolymorphicConverter<T> : JsonConverter<T>
{


    private readonly ITypeProvider _typeProvider;
    private readonly string _discriminatorField;
    private readonly string _discriminatedField;

    public PolymorphicConverter(TypeDiscrimnatorAttribute typeDiscriminator, string discriminatedField) : base()
    {
        _discriminatorField = typeDiscriminator.FieldName;
        _typeProvider = (ITypeProvider)typeDiscriminator.ConverterType.GetConstructor(new Type[] { }).Invoke(null);
        _discriminatedField = discriminatedField;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        // yes if we find the attribute on one of the propertes
        // and there is no other Converter

        return base.CanConvert(typeToConvert);
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var constructorInfo = typeof(T).GetConstructors().First();
        var parameters = constructorInfo.GetParameters().ToDictionary(x => x.Name);
        var constructor = EntityConverter.BuildConstructerFrom(constructorInfo);
        using (var jsonDocument = JsonDocument.ParseValue(ref reader))
        {
            var discriminatorName = options.PropertyNamingPolicy.ConvertName(_discriminatorField);
            var discriminatorValue = jsonDocument.RootElement.GetProperty(discriminatorName).GetRawText();
            var discriminatorType = parameters[_discriminatorField].ParameterType;
            var discriminatorTypedValue = JsonSerializer.Deserialize(discriminatorValue, discriminatorType, options);
            var discriminatedType = _typeProvider.GetTypeInfo(discriminatorTypedValue);
            var constructorParams =
                constructorInfo.GetParameters().Select(p =>
                {
                    var parameterType = p.Name == _discriminatedField ? discriminatedType : p.ParameterType;
                    var fName = options.PropertyNamingPolicy?.ConvertName(p.Name) ?? p.Name;
                    var prop = jsonDocument.RootElement.GetProperty(fName);
                    return JsonSerializer.Deserialize(prop.GetRawText(), parameterType, options);

                }).ToArray();

            return (T?)constructor(constructorParams);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions());
        var thisConverter = newOptions.Converters.FirstOrDefault(c => c.GetType() == typeof(PolymorphicConverterFactory));
        if (thisConverter != null)
        {
            newOptions.Converters.Remove(thisConverter);
        }
        JsonSerializer.Serialize(writer, value, newOptions);
    }
}