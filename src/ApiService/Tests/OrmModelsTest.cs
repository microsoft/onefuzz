using FsCheck;
using FsCheck.Xunit;
using Xunit.Abstractions;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Security;

namespace Tests
{

    public class OrmGenerators
    {

        public static Gen<BaseEvent> BaseEvent()
        {
            return Gen.OneOf(new[] {
                Arb.Generate<EventNodeHeartbeat>().Select(e => e as BaseEvent),
                Arb.Generate<EventTaskHeartbeat>().Select(e => e as BaseEvent),
                Arb.Generate<EventInstanceConfigUpdated>().Select(e => e as BaseEvent)
            });
        }


        public static Gen<Uri> Uri()
        {
            return Arb.Generate<IPv4Address>().Select(
                arg => new Uri($"https://{arg.Item.ToString()}:8080")
            );
        }

        public static Gen<WebhookMessageLog> WebhookMessageLog()
        {
            return Arb.Generate<Tuple<Tuple<Guid, EventType, BaseEvent, Guid, string, Guid>, Tuple<WebhookMessageState, int>>>().Select(
                    arg => new WebhookMessageLog(
                        EventId: arg.Item1.Item1,
                        EventType: arg.Item1.Item2,
                        Event: arg.Item1.Item3,
                        InstanceId: arg.Item1.Item4,
                        InstanceName: arg.Item1.Item5,
                        WebhookId: arg.Item1.Item6,
                        State: arg.Item2.Item1,
                        TryCount: arg.Item2.Item2
            ));
        }

        public static Gen<Node> Node()
        {
            return Arb.Generate<Tuple<Tuple<DateTimeOffset?, string, Guid?, Guid, NodeState>, Tuple<Guid?, DateTimeOffset, string, bool, bool, bool>>>().Select(
                arg => new Node(
                        InitializedAt: arg.Item1.Item1,
                        PoolName: arg.Item1.Item2,
                        PoolId: arg.Item1.Item3,
                        MachineId: arg.Item1.Item4,
                        State: arg.Item1.Item5,
                        ScalesetId: arg.Item2.Item1,
                        Heartbeat: arg.Item2.Item2,
                        Version: arg.Item2.Item3,
                        ReimageRequested: arg.Item2.Item4,
                        DeleteRequested: arg.Item2.Item5,
                        DebugKeepNode: arg.Item2.Item6));
        }

        public static Gen<ProxyForward> ProxyForward()
        {
            return Arb.Generate<Tuple<string, int, int, IPv4Address>>().Select(
                arg =>
                    new ProxyForward(
                        Region: arg.Item1,
                        DstPort: arg.Item2,
                        SrcPort: arg.Item3,
                        DstIp: arg.Item4.Item.ToString()
                    )
            );
        }

        public static Gen<Proxy> Proxy()
        {
            return Arb.Generate<Tuple<Tuple<string, Guid, DateTimeOffset?, VmState, Authentication, string?, Error?>, Tuple<string, ProxyHeartbeat?>>>().Select(
                arg =>
                    new Proxy(
                        Region: arg.Item1.Item1,
                        ProxyId: arg.Item1.Item2,
                        CreatedTimestamp: arg.Item1.Item3,
                        State: arg.Item1.Item4,
                        Auth: arg.Item1.Item5,
                        Ip: arg.Item1.Item6,
                        Error: arg.Item1.Item7,
                        Version: arg.Item2.Item1,
                        Heartbeat: arg.Item2.Item2
                    )
            );
        }

        public static Gen<EventMessage> EventMessage()
        {
            return Arb.Generate<Tuple<Guid, BaseEvent, Guid, string>>().Select(
                arg =>
                    new EventMessage(
                        EventId: arg.Item1,
                        EventType: arg.Item2.GetEventType(),
                        Event: arg.Item2,
                        InstanceId: arg.Item3,
                        InstanceName: arg.Item4
                    )
            );
        }

        public static Gen<NetworkConfig> NetworkConfig()
        {
            return Arb.Generate<Tuple<IPv4Address, IPv4Address>>().Select(
                arg =>
                    new NetworkConfig(
                        AddressSpace: arg.Item1.Item.ToString(),
                        Subnet: arg.Item2.Item.ToString()
                    )
            );
        }

        public static Gen<NetworkSecurityGroupConfig> NetworkSecurityGroupConfig()
        {
            return Arb.Generate<Tuple<string[], IPv4Address[]>>().Select(
                arg =>
                    new NetworkSecurityGroupConfig(
                        AllowedServiceTags: arg.Item1,
                        AllowedIps: (from ip in arg.Item2 select ip.Item.ToString()).ToArray()
                    )
            );
        }

        public static Gen<InstanceConfig> InstanceConfig()
        {
            return Arb.Generate<Tuple<
                Tuple<string, Guid[]?, bool, string[], NetworkConfig, NetworkSecurityGroupConfig, AzureVmExtensionConfig?>,
                Tuple<string, IDictionary<string, ApiAccessRule>?, IDictionary<Guid, Guid[]>?, IDictionary<string, string>?, IDictionary<string, string>?>>>().Select(
                arg =>
                    new InstanceConfig(
                        InstanceName: arg.Item1.Item1,
                        Admins: arg.Item1.Item2,
                        AllowPoolManagement: arg.Item1.Item3,
                        AllowedAadTenants: arg.Item1.Item4,
                        NetworkConfig: arg.Item1.Item5,
                        ProxyNsgConfig: arg.Item1.Item6,
                        Extensions: arg.Item1.Item7,

                        ProxyVmSku: arg.Item2.Item1,
                        ApiAccessRules: arg.Item2.Item2,
                        GroupMembership: arg.Item2.Item3,
                        VmTags: arg.Item2.Item4,
                        VmssTags: arg.Item2.Item5
                    )
            );
        }

        public static Gen<Task> Task()
        {
            return Arb.Generate<Tuple<
                    Tuple<Guid, Guid, TaskState, Os, TaskConfig, Error?, Authentication?>,
                    Tuple<DateTimeOffset?, DateTimeOffset?, UserInfo?>>>().Select(
                    arg =>
                        new Task(
                            JobId: arg.Item1.Item1,
                            TaskId: arg.Item1.Item2,
                            State: arg.Item1.Item3,
                            Os: arg.Item1.Item4,
                            Config: arg.Item1.Item5,
                            Error: arg.Item1.Item6,
                            Auth: arg.Item1.Item7,

                            Heartbeat: arg.Item2.Item1,
                            EndTime: arg.Item2.Item2,
                            UserInfo: arg.Item2.Item3
                        )
                );
        }


        public static Gen<Scaleset> Scaleset()
        {
            return Arb.Generate<Tuple<
                Tuple<string, Guid, ScalesetState, Authentication?, string, string, string>,
                Tuple<int, bool, bool, bool, List<ScalesetNodeState>, Guid?, Guid?>,
                Tuple<Dictionary<string, string>>>>().Select(
                    arg =>
                        new Scaleset(
                            PoolName: arg.Item1.Item1,
                            ScalesetId: arg.Item1.Item2,
                            State: arg.Item1.Item3,
                            Auth: arg.Item1.Item4,
                            VmSku: arg.Item1.Item5,
                            Image: arg.Item1.Item6,
                            Region: arg.Item1.Item7,

                            Size: arg.Item2.Item1,
                            SpotInstance: arg.Item2.Item2,
                            EphemeralOsDisks: arg.Item2.Item3,
                            NeedsConfigUpdate: arg.Item2.Item4,
                            Nodes: arg.Item2.Item5,
                            ClientId: arg.Item2.Item6,
                            ClientObjectId: arg.Item2.Item7,

                            Tags: arg.Item3.Item1
                        )
                );
        }


        public static Gen<Webhook> Webhook()
        {
            return Arb.Generate<Tuple<Guid, string, Uri?, List<EventType>, string, WebhookMessageFormat>>().Select(
                arg =>
                    new Webhook(
                        WebhookId: arg.Item1,
                        Name: arg.Item2,
                        Url: arg.Item3,
                        EventTypes: arg.Item4,
                        SecretToken: arg.Item5,
                        MessageFormat: arg.Item6
                    )
                );

        }
    }

    public class OrmArb
    {
        public static Arbitrary<Uri> Uri()
        {
            return Arb.From(OrmGenerators.Uri());
        }

        public static Arbitrary<BaseEvent> BaseEvent()
        {
            return Arb.From(OrmGenerators.BaseEvent());
        }

        public static Arbitrary<Node> Node()
        {
            return Arb.From(OrmGenerators.Node());
        }

        public static Arbitrary<ProxyForward> ProxyForward()
        {
            return Arb.From(OrmGenerators.ProxyForward());
        }

        public static Arbitrary<Proxy> Proxy()
        {
            return Arb.From(OrmGenerators.Proxy());
        }

        public static Arbitrary<EventMessage> EventMessage()
        {
            return Arb.From(OrmGenerators.EventMessage());
        }

        public static Arbitrary<NetworkConfig> NetworkConfig()
        {
            return Arb.From(OrmGenerators.NetworkConfig());
        }

        public static Arbitrary<NetworkSecurityGroupConfig> NetworkSecurityConfig()
        {
            return Arb.From(OrmGenerators.NetworkSecurityGroupConfig());
        }

        public static Arbitrary<InstanceConfig> InstanceConfig()
        {
            return Arb.From(OrmGenerators.InstanceConfig());
        }

        public static Arbitrary<WebhookMessageLog> WebhookMessageLog()
        {
            return Arb.From(OrmGenerators.WebhookMessageLog());
        }

        public static Arbitrary<Task> Task()
        {
            return Arb.From(OrmGenerators.Task());
        }

        public static Arbitrary<Scaleset> Scaleset()
        {
            return Arb.From(OrmGenerators.Scaleset());
        }

        public static Arbitrary<Webhook> Webhook()
        {
            return Arb.From(OrmGenerators.Webhook());
        }
    }


    public class OrmModelsTest
    {
        EntityConverter _converter = new EntityConverter();
        ITestOutputHelper _output;

        private HashSet<Type> _baseTypes = new HashSet<Type>(
            new[]{
            typeof(byte),
            typeof(char),
            typeof(bool),
            typeof(int),
            typeof(long),
            typeof(float),
            typeof(double),
            typeof(string),
            typeof(Guid),
            typeof(Uri),
            typeof(DateTime),
            typeof(DateTime?),
            typeof(DateTimeOffset),
            typeof(DateTimeOffset?),
            typeof(SecureString)
        });

        public OrmModelsTest(ITestOutputHelper output)
        {
            Arb.Register<OrmArb>();
            _output = output;
        }
        bool IEnumerableEqual<T>(IEnumerable<T>? a, IEnumerable<T>? b)
        {
            if (a is null && b is null)
            {
                return true;
            }
            if (a!.Count() != b!.Count())
            {
                return false;
            }

            if (a!.Count() == 0 && b!.Count() == 0)
            {
                return true;
            }

            foreach (var v in a!.Zip(b!))
            {
                if (!AreEqual(v.First, v.Second))
                {
                    return false;
                }
            }

            return true;
        }

        bool IDictionaryEqual<TKey, TValue>(IDictionary<TKey, TValue>? a, IDictionary<TKey, TValue>? b, Func<TValue, TValue, bool> cmp)
        {
            if (a is null && b is null)
                return true;

            if (a!.Count == 0 && b!.Count == 0)
                return true;

            if (a!.Count != b!.Count)
                return false;

            return a!.Any(v => cmp(v.Value, b[v.Key]));
        }

        bool IDictionaryEqual<TKey, TValue>(IDictionary<TKey, TValue>? a, IDictionary<TKey, TValue>? b)
        {
            if (a is null && b is null)
                return true;

            if (a!.Count == 0 && b!.Count == 0)
                return true;

            if (a!.Count != b!.Count)
                return false;

            return a!.Any(v => AreEqual(v.Value, b[v.Key]));
        }


        private bool AreEqual<T>(T r1, T r2)
        {
            var t = typeof(T);

            if (r1 is null && r2 is null)
                return true;

            if (_baseTypes.Contains(t))
                return r1!.Equals(r2);

            foreach (var p in t.GetProperties())
            {
                var v1 = p.GetValue(r1);
                var v2 = p.GetValue(r2);
                var tt = p.PropertyType;

                if (v1 is null && v2 is null)
                    continue;

                if (_baseTypes.Contains(tt) && !v1!.Equals(v2))
                    return false;

                if (tt.GetInterface("IEnumerable") is not null)
                {
                    if (!IEnumerableEqual(v1 as IEnumerable<Object>, v2 as IEnumerable<Object>))
                        return false;
                }

                if (tt.GetInterface("IDictionary") is not null)
                {
                    if (!IDictionaryEqual(v1 as IDictionary<Object, Object>, v2 as IDictionary<Object, Object>))
                        return false;
                }
            }
            return true;
        }


        [Property]
        public bool Node(Node node)
        {
            var entity = _converter.ToTableEntity(node);
            var node2 = _converter.ToRecord<Node>(entity);
            return AreEqual(node2, node);
        }

        [Property]
        public bool ProxyForward(ProxyForward proxyForward)
        {
            var entity = _converter.ToTableEntity(proxyForward);
            var r = _converter.ToRecord<ProxyForward>(entity);
            return AreEqual(proxyForward, r);
        }

        [Property]
        public bool Proxy(Proxy proxy)
        {
            var entity = _converter.ToTableEntity(proxy);
            var r = _converter.ToRecord<Proxy>(entity);
            return AreEqual(proxy, r);
        }

        [Property]
        public bool Task(Task task)
        {
            var entity = _converter.ToTableEntity(task);
            var r = _converter.ToRecord<Task>(entity);
            return AreEqual(r, task);
        }


        [Property]
        public bool InstanceConfig(InstanceConfig cfg)
        {
            var entity = _converter.ToTableEntity(cfg);
            var r = _converter.ToRecord<InstanceConfig>(entity);
            return AreEqual(cfg, r);
        }

        [Property]
        public bool Scaleset(Scaleset ss)
        {
            var entity = _converter.ToTableEntity(ss);
            var r = _converter.ToRecord<Scaleset>(entity);
            return AreEqual(ss, r);
        }

        /*
        [Property]
        public bool WebhookMessageLog(WebhookMessageLog log)
        {
            var entity = _converter.ToTableEntity(log);
            var r = _converter.ToRecord<WebhookMessageLog>(entity);
            return AreEqual(r, log);
        }
        */


        [Property]
        public bool Webhook(Webhook wh)
        {
            var entity = _converter.ToTableEntity(wh);
            var r = _converter.ToRecord<Webhook>(entity);
            return AreEqual(wh, r);
        }



        //Sample function on how repro a failing test run, using Replay
        //functionality of FsCheck. Feel free to 
        /*
        [Property]
        void Replay()
        {
            var seed = FsCheck.Random.StdGen.NewStdGen(1384212554,297026222);
            var p = Prop.ForAll((Task x) => Task(x) );
            p.Check(new Configuration { Replay = seed });
        }

        */
    }
}