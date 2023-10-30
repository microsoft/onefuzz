﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Azure.Core.Serialization;
using FluentAssertions;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;

namespace Tests;

// This class contains tests for serialization and
// deserialization of examples generated by the
// onefuzz-agent’s `debug` sub-command. We test each
// example for roundtripping which ensures that no
// data is lost upon deserialization.
//
// We could set this up to run onefuzz-agent itself
// but that seems like additional unnecessary complexity;
// at the moment the Rust code is not built when building C#.
public class RequestsTests {

    private readonly JsonObjectSerializer _serializer = new(serializationOptions());

    private static JsonSerializerOptions serializationOptions() {
        // base on the serialization options used at runtime, but
        // also indent to match inputs:
        return new JsonSerializerOptions(EntityConverter.GetJsonSerializerOptions()) {
            WriteIndented = true
        };
    }

    private void AssertRoundtrips<T>(string json) {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserialized = (T?)_serializer.Deserialize(stream, typeof(T), CancellationToken.None);
        var reserialized = _serializer.Serialize(deserialized);
        var result = Encoding.UTF8.GetString(reserialized);
        result = result.Replace(System.Environment.NewLine, "\n");
        json = json.Replace(System.Environment.NewLine, "\n");
        Assert.Equal(json, result);
    }

    // Finds all non-nullable properties exposed on request objects (inheriting from BaseRequest).
    // Note that at the moment we do not validate inner types since we are reusing some model types
    // as request objects/DTOs, which we should stop doing.
    public static IEnumerable<object[]> NonNullableRequestProperties() {
        var baseType = typeof(BaseRequest);
        var asm = baseType.Assembly;
        foreach (var requestType in asm.GetTypes().Where(t => t.IsAssignableTo(baseType))) {
            if (requestType == baseType) {
                continue;
            }

            foreach (var property in requestType.GetProperties()) {
                var nullabilityContext = new NullabilityInfoContext();
                var nullability = nullabilityContext.Create(property);
                if (nullability.ReadState == NullabilityState.NotNull) {
                    yield return new object[] { requestType, property };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(NonNullableRequestProperties))]
    public void EnsureRequiredAttributesExistsOnNonNullableRequestProperties(Type requestType, PropertyInfo property) {
        if (!property.IsDefined(typeof(RequiredAttribute))) {
            // if not required it must have a default

            // find appropriate parameter
            var param = requestType.GetConstructors().Single().GetParameters().Single(p => p.Name == property.Name);
            Assert.True(param.HasDefaultValue,
                "For request types, all non-nullable properties should either have a default value, or the [Required] attribute."
            );
        } else {
            // it is required, okay
        }
    }

    [Fact]
    public void NodeEvent_WorkerEvent_Done() {
        // generated with: onefuzz-agent debug node_event worker_event done

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""worker_event"": {
      ""done"": {
        ""job_id"": ""40a6e135-b6e0-4dc4-837d-0401db0061fb"",
        ""task_id"": ""00e1b131-e2a1-444d-8cc6-841e6cd48f93"",
        ""exit_status"": {
          ""code"": 0,
          ""signal"": null,
          ""success"": true
        },
        ""stderr"": ""stderr output goes here"",
        ""stdout"": ""stdout output goes here""
      }
    }
  },
  ""machine_id"": ""5ccbe157-a84c-486a-8171-d213fba27247""
}");
    }

    [Fact]
    public void NodeEvent_WorkerEvent_Running() {
        // generated with: onefuzz-agent debug node_event worker_event running

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""worker_event"": {
      ""running"": {
        ""job_id"": ""a46bf12b-1837-48a6-b6a1-4e4b1c371c25"",
        ""task_id"": ""1763e113-02a0-4a3e-b477-92762f030d95""
      }
    }
  },
  ""machine_id"": ""e819efa5-c43f-46a2-bf9e-cc6a6de86ef9""
}");
    }

    [Fact]
    public void NodeEvent_StateUpdate_Init() {
        // generated with: onefuzz-agent debug node_event state_update '"init"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""init""
    }
  },
  ""machine_id"": ""38bd035b-fa5b-4cbc-9037-aa4e6550f713""
}");
    }

    [Fact]
    public void NodeEvent_StateUpdate_Free() {
        // generated with: onefuzz-agent debug node_event state_update '"free"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""free""
    }
  },
  ""machine_id"": ""09a0cd4c-a918-4777-98b6-617e42084eb1""
}");
    }

    [Fact]
    public void NodeEvent_StateUpdate_SettingUp() {
        // generated with: onefuzz-agent debug node_event state_update setting-up

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""setting_up"",
      ""data"": {
        ""tasks"": null,
        ""task_data"": [
          {
            ""job_id"": ""b99d0d26-cb46-48af-8770-4768e1262d1c"",
            ""task_id"": ""f78f8b2d-3ce1-466e-968b-c61fb9d49d58""
          },
          {
            ""job_id"": ""dee926cf-a20a-4e6f-b806-324e64b07243"",
            ""task_id"": ""61178115-34d8-43d2-8ee0-47f065bd7f74""
          }
        ]
      }
    }
  },
  ""machine_id"": ""82da6784-fd8c-426a-8baf-643654a060d8""
}");
    }


    [Fact]
    public void NodeEvent_StateUpdate_Rebooting() {
        // generated with: onefuzz-agent debug node_event state_update '"rebooting"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""rebooting""
    }
  },
  ""machine_id"": ""8825ca94-11d9-4e83-9df0-c052ee8b77c8""
}");
    }


    [Fact]
    public void NodeEvent_StateUpdate_Ready() {
        // generated with: onefuzz-agent debug node_event state_update '"ready"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""ready""
    }
  },
  ""machine_id"": ""a98f9a27-cfb9-426b-a6f2-5b2c04268697""
}");
    }


    [Fact]
    public void NodeEvent_StateUpdate_Busy() {
        // generated with: onefuzz-agent debug node_event state_update '"busy"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""busy""
    }
  },
  ""machine_id"": ""e4c70423-bb5c-40a9-9645-942243738240""
}");
    }


    [Fact]
    public void NodeEvent_StateUpdate_Done() {
        // generated with: onefuzz-agent debug node_event state_update '"done"'

        AssertRoundtrips<NodeStateEnvelope>(@"{
  ""event"": {
    ""state_update"": {
      ""state"": ""done"",
      ""data"": {
        ""script_output"": {
          ""exit_status"": null,
          ""stderr"": ""err"",
          ""stdout"": ""out""
        }
      }
    }
  },
  ""machine_id"": ""5284cba4-aa7a-4285-b2b8-d5123c182bc3""
}");
    }
}
