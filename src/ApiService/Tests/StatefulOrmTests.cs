using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ApiService.OneFuzzLib.Orm;
using Xunit;

namespace Tests;

public class StatefulOrmTests {
    public static IEnumerable<object[]> GetStatefulOrmTypes() {
        var statefulOrmType = typeof(StatefulOrm<,,>);
        foreach (var type in statefulOrmType.Assembly.GetTypes()) {
            var baseType = type.BaseType;
            if (baseType is not null
                && baseType.IsGenericType
                && baseType.GetGenericTypeDefinition() == statefulOrmType) {
                yield return new object[] { type };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetStatefulOrmTypes))]
    public void EnsureValid(Type statefulOrmType) {
        // make sure that the classes are all valid,
        // there is a static constructor on StatefulOrm that performs checks,
        // so explicitly run the class constructor for the base type:
        RuntimeHelpers.RunClassConstructor(statefulOrmType.BaseType!.TypeHandle);
    }
}
