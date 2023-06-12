using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit.Abstractions;

namespace Tests {

    public class OrmGenerators {
        public static Gen<BaseEvent> BaseEvent() {
            return Gen.OneOf(new[] {
                Arb.Generate<EventNodeHeartbeat>().Select(e => e as BaseEvent),
                Arb.Generate<EventTaskHeartbeat>().Select(e => e as BaseEvent),
                Arb.Generate<EventInstanceConfigUpdated>().Select(e => e as BaseEvent),
                Arb.Generate<EventProxyCreated>().Select(e => e as BaseEvent),
                Arb.Generate<EventProxyDeleted>().Select(e => e as BaseEvent),
                Arb.Generate<EventProxyFailed>().Select(e => e as BaseEvent),
                Arb.Generate<EventProxyStateUpdated>().Select(e => e as BaseEvent),
                Arb.Generate<EventCrashReported>().Select(e => e as BaseEvent),
                Arb.Generate<EventRegressionReported>().Select(e => e as BaseEvent),
                Arb.Generate<EventFileAdded>().Select(e => e as BaseEvent),
                Arb.Generate<EventTaskFailed>().Select(e => e as BaseEvent),
                Arb.Generate<EventTaskStopped>().Select(e => e as BaseEvent),
                Arb.Generate<EventTaskStateUpdated>().Select(e => e as BaseEvent),
                Arb.Generate<EventScalesetFailed>().Select(e => e as BaseEvent),
                Arb.Generate<EventScalesetResizeScheduled>().Select(e => e as BaseEvent),
                Arb.Generate<EventScalesetStateUpdated>().Select(e => e as BaseEvent),
                Arb.Generate<EventNodeDeleted>().Select(e => e as BaseEvent),
                Arb.Generate<EventNodeCreated>().Select(e => e as BaseEvent),
            });
        }

        public static Gen<Uri> Uri() {
            return Arb.Generate<IPv4Address>().Select(
                arg => new Uri($"https://{arg.Item.ToString()}:8080")
            );
        }

        public static Gen<ISecret<T>> ISecret<T>() {
            if (typeof(T) == typeof(string)) {
                return Arb.Generate<string>().Select(s => (ISecret<T>)new SecretAddress<string>(new Uri("http://test")));
            }

            if (typeof(T) == typeof(GithubAuth)) {
                return Arb.Generate<GithubAuth>().Select(s => (ISecret<T>)new SecretAddress<T>(new Uri("http://test")));
            } else {
                throw new Exception($"Unsupported secret type {typeof(T)}");
            }
        }

        public static Gen<Version> Version() {
            //OneFuzz version uses 3 number version
            return Arb.Generate<Tuple<UInt16, UInt16, UInt16>>().Select(
                arg =>
                    new Version(arg.Item1, arg.Item2, arg.Item3)
                );
        }

        public static Gen<WebhookMessageLog> WebhookMessageLog() {
            return Arb.Generate<Tuple<Tuple<Guid, BaseEvent, Guid, string, Guid>, Tuple<WebhookMessageState, int>>>().Select(
                    arg => new WebhookMessageLog(
                        EventId: arg.Item1.Item1,
                        EventType: arg.Item1.Item2.GetEventType(),
                        Event: arg.Item1.Item2,
                        InstanceId: arg.Item1.Item3,
                        InstanceName: arg.Item1.Item4,
                        WebhookId: arg.Item1.Item5,
                        State: arg.Item2.Item1,
                        TryCount: arg.Item2.Item2
            ));
        }

        public static Gen<NodeTasks> NodeTasks() {
            return Arb.Generate<Tuple<Guid, Guid, NodeTaskState>>().Select(
                arg =>
                    new NodeTasks(
                        MachineId: arg.Item1,
                        TaskId: arg.Item2,
                        State: arg.Item3
                    )
            );
        }

        public static Gen<PoolName> PoolNameGen { get; }
            = from name in Arb.Generate<NonEmptyString>()
              where PoolName.IsValid(name.Get)
              select PoolName.Parse(name.Get);

        public static Gen<ScalesetId> ScalesetIdGen { get; }
            = from name in Arb.Generate<NonEmptyString>()
              where ScalesetId.IsValid(name.Get)
              select ScalesetId.Parse(name.Get);

        public static Gen<Region> RegionGen { get; }
            = from name in Arb.Generate<NonEmptyString>()
              where Region.IsValid(name.Get)
              select Region.Parse(name.Get);

        public static Gen<Node> Node { get; }
            = from arg in Arb.Generate<Tuple<Tuple<DateTimeOffset?, Guid?, Guid, NodeState>, Tuple<DateTimeOffset, string, bool, bool, bool>>>()
              from poolName in PoolNameGen
              from scalesetId in Arb.Generate<Guid>()
              select new Node(
                        InitializedAt: arg.Item1.Item1,
                        PoolName: poolName,
                        PoolId: arg.Item1.Item3,
                        MachineId: arg.Item1.Item3,
                        State: arg.Item1.Item4,
                        ScalesetId: ScalesetId.Parse(scalesetId.ToString()),
                        Heartbeat: arg.Item2.Item1,
                        Version: arg.Item2.Item2,
                        ReimageRequested: arg.Item2.Item3,
                        DeleteRequested: arg.Item2.Item4,
                        DebugKeepNode: arg.Item2.Item5);

        public static Gen<ProxyForward> ProxyForward { get; } =
            from region in RegionGen
            from port in Gen.Choose(0, ushort.MaxValue)
            from scalesetId in Arb.Generate<Guid>()
            from machineId in Arb.Generate<Guid>()
            from proxyId in Arb.Generate<Guid?>()
            from dstPort in Gen.Choose(0, ushort.MaxValue)
            from dstIp in Arb.Generate<IPv4Address>()
            from endTime in Arb.Generate<DateTimeOffset>()
            select new ProxyForward(
                Region: region,
                Port: port,
                ScalesetId: ScalesetId.Parse(scalesetId.ToString()),
                MachineId: machineId,
                ProxyId: proxyId,
                DstPort: dstPort,
                DstIp: dstIp.ToString(),
                EndTime: endTime);

        public static Gen<Proxy> Proxy { get; } =
            from region in RegionGen
            from proxyId in Arb.Generate<Guid>()
            from createdTimestamp in Arb.Generate<DateTimeOffset?>()
            from state in Arb.Generate<VmState>()
            from ip in Arb.Generate<string>()
            from error in Arb.Generate<Error?>()
            from version in Arb.Generate<string>()
            from heartbeat in Arb.Generate<ProxyHeartbeat?>()
            from outdated in Arb.Generate<bool>()
            select new Proxy(
                Region: region,
                ProxyId: proxyId,
                CreatedTimestamp: createdTimestamp,
                State: state,
                Auth: new SecretAddress<Authentication>(new System.Uri("http://test")),
                Ip: ip,
                Error: error,
                Version: version,
                Heartbeat: heartbeat,
                Outdated: outdated);

        public static Gen<EventMessage> EventMessage() {
            return Arb.Generate<Tuple<Guid, BaseEvent, Guid, string, DateTime>>().Select(
                arg =>
                    new EventMessage(
                        EventId: arg.Item1,
                        EventType: arg.Item2.GetEventType(),
                        Event: arg.Item2,
                        InstanceId: arg.Item3,
                        InstanceName: arg.Item4,
                        CreatedAt: arg.Item5
                    )
            );
        }

        public static Gen<NetworkConfig> NetworkConfig() {
            return Arb.Generate<Tuple<IPv4Address, IPv4Address>>().Select(
                arg =>
                    new NetworkConfig(
                        AddressSpace: arg.Item1.Item.ToString(),
                        Subnet: arg.Item2.Item.ToString()
                    )
            );
        }

        public static Gen<NetworkSecurityGroupConfig> NetworkSecurityGroupConfig() {
            return Arb.Generate<Tuple<string[], IPv4Address[]>>().Select(
                arg =>
                    new NetworkSecurityGroupConfig(
                        AllowedServiceTags: arg.Item1,
                        AllowedIps: (from ip in arg.Item2 select ip.Item.ToString()).ToArray()
                    )
            );
        }

        public static Gen<InstanceConfig> InstanceConfig() {
            var config = Arb.Generate<Tuple<
                Tuple<string, Guid[]?, string[], NetworkConfig, NetworkSecurityGroupConfig, AzureVmExtensionConfig?, NonNull<string>>,
                Tuple<bool, IDictionary<string, ApiAccessRule>?, IDictionary<Guid, Guid[]>?, IDictionary<string, string>?, IDictionary<string, string>?>>>().Select(
                arg =>
                    new InstanceConfig(
                        InstanceName: arg.Item1.Item1,
                        Admins: arg.Item1.Item2,
                        AllowedAadTenants: arg.Item1.Item3,
                        NetworkConfig: arg.Item1.Item4,
                        ProxyNsgConfig: arg.Item1.Item5,
                        Extensions: arg.Item1.Item6,
                        ProxyVmSku: arg.Item1.Item7.Item,

                        RequireAdminPrivileges: arg.Item2.Item1,
                        ApiAccessRules: arg.Item2.Item2,
                        GroupMembership: arg.Item2.Item3,
                        VmTags: arg.Item2.Item4,
                        VmssTags: arg.Item2.Item5
                    )
                );
            return config;
        }

        public static Gen<Task> Task() {
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
                            Auth: new SecretAddress<Authentication>(new Uri("http://test")),

                            Heartbeat: arg.Item2.Item1,
                            EndTime: arg.Item2.Item2,
                            UserInfo: arg.Item2.Item3
                        )
                );
        }

        public static Gen<ImageReference> ImageReferenceGen { get; } =
            Gen.Elements(
                ImageReference.MustParse("Canonical:UbuntuServer:20.04-LTS:latest"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/images/imageName"));

        public static Gen<Scaleset> Scaleset { get; }
            = from arg in Arb.Generate<Tuple<
                    Tuple<ScalesetState, Authentication?, string>,
                    Tuple<int, bool, bool, bool, Error?, Guid?>,
                    Tuple<Guid?, Dictionary<string, string>>>>()
              from scalesetId in Arb.Generate<Guid>()
              from poolName in PoolNameGen
              from region in RegionGen
              from image in ImageReferenceGen
              select new Scaleset(
                          PoolName: poolName,
                          ScalesetId: ScalesetId.Parse(scalesetId.ToString()),
                          State: arg.Item1.Item1,
                          Auth: new SecretAddress<Authentication>(new Uri("http://test")),
                          VmSku: arg.Item1.Item3,
                          Image: image,
                          Region: region,

                          Size: arg.Item2.Item1,
                          SpotInstances: arg.Item2.Item2,
                          EphemeralOsDisks: arg.Item2.Item3,
                          NeedsConfigUpdate: arg.Item2.Item4,
                          Error: arg.Item2.Item5,
                          ClientId: arg.Item2.Item6,

                          ClientObjectId: arg.Item3.Item1,
                          Tags: arg.Item3.Item2);

        public static Gen<Webhook> Webhook() {
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

        public static Gen<WebhookMessage> WebhookMessage() {
            return Arb.Generate<Tuple<Guid, BaseEvent, Guid, string, Guid, DateTime, Uri>>().Select(
                arg =>
                    new WebhookMessage(
                        EventId: arg.Item1,
                        EventType: arg.Item2.GetEventType(),
                        Event: arg.Item2,
                        InstanceId: arg.Item3,
                        InstanceName: arg.Item4,
                        WebhookId: arg.Item5,
                        CreatedAt: arg.Item6,
                        SasUrl: arg.Item7
                    )
            );
        }

        public static Gen<WebhookMessageEventGrid> WebhookMessageEventGrid() {
            return Arb.Generate<Tuple<string, string, BaseEvent, Guid, DateTimeOffset, Uri>>().Select(
                arg =>
                    new WebhookMessageEventGrid(
                        DataVersion: arg.Item1,
                        Subject: arg.Item2,
                        EventType: arg.Item3.GetEventType(),
                        Data: arg.Item3,
                        Id: arg.Item4,
                        EventTime: arg.Item5,
                        SasUrl: arg.Item6
                    )
            );
        }

        public static Gen<Report> Report() {
            return Arb.Generate<Tuple<string, BlobRef, List<string>, Guid, int, Uri?>>().Select(
                arg =>
                    new Report(
                        InputUrl: arg.Item1,
                        InputBlob: arg.Item2,
                        Executable: arg.Item1,
                        CrashType: arg.Item1,
                        CrashSite: arg.Item1,
                        CallStack: arg.Item3,
                        CallStackSha256: arg.Item1,
                        InputSha256: arg.Item1,
                        AsanLog: arg.Item1,
                        TaskId: arg.Item4,
                        JobId: arg.Item4,
                        ScarinessScore: arg.Item5,
                        ScarinessDescription: arg.Item1,
                        MinimizedStack: arg.Item3,
                        MinimizedStackSha256: arg.Item1,
                        MinimizedStackFunctionNames: arg.Item3,
                        MinimizedStackFunctionNamesSha256: arg.Item1,
                        MinimizedStackFunctionLines: arg.Item3,
                        MinimizedStackFunctionLinesSha256: arg.Item1,
                        ToolName: arg.Item1,
                        ToolVersion: arg.Item1,
                        OnefuzzVersion: arg.Item1,
                        ReportUrl: arg.Item6

                    )
            );
        }

        public static Gen<NoReproReport> NoReproReport() {
            return Arb.Generate<Tuple<string, BlobRef?, string?, Guid, int>>().Select(
                arg =>
                    new NoReproReport(
                        arg.Item1,
                        arg.Item2,
                        arg.Item3,
                        arg.Item4,
                        arg.Item4,
                        arg.Item5,
                        arg.Item3
                    )
            );
        }

        public static Gen<CrashTestResult> CrashTestResult() {
            return Arb.Generate<Tuple<Report, NoReproReport>>().Select(
                arg =>
                    new CrashTestResult(
                        arg.Item1,
                        arg.Item2
                    )
            );
        }

        public static Gen<RegressionReport> RegressionReport() {
            return Arb.Generate<Tuple<CrashTestResult, CrashTestResult?, Uri?>>().Select(
                arg =>
                    new RegressionReport(
                        arg.Item1,
                        arg.Item2,
                        arg.Item3
                    )
            );
        }

        public static Gen<Container> ContainerGen { get; } =
            from len in Gen.Choose(3, 63)
            from name in Gen.ArrayOf(len, Gen.Elements<char>("abcdefghijklmnopqrstuvwxyz0123456789-"))
            let nameString = new string(name)
            where Container.IsValid(nameString)
            select Container.Parse(nameString);

        public static Gen<ADODuplicateTemplate> AdoDuplicateTemplate() {
            return Arb.Generate<Tuple<List<string>, Dictionary<string, string>, string?>>().Select(
                arg =>
                    new ADODuplicateTemplate(
                        arg.Item1,
                        arg.Item2,
                        arg.Item2,
                        arg.Item3
                    )
            );
        }

        public static Gen<AdoTemplate> AdoTemplate() {
            return Arb.Generate<Tuple<Uri, SecretData<string>, NonEmptyString, List<string>, Dictionary<string, string>, ADODuplicateTemplate, string?>>().Select(
                arg =>
                    new AdoTemplate(
                        arg.Item1,
                        arg.Item2,
                        arg.Item3.Item,
                        arg.Item3.Item,
                        arg.Item4,
                        arg.Item5,
                        AdoDuplicateTemplate().Sample(1, 1).First(),
                        arg.Item7
                    )
            );
        }

        public static Gen<TeamsTemplate> TeamsTemplate() {
            return Arb.Generate<Tuple<SecretData<string>>>().Select(
                arg =>
                    new TeamsTemplate(
                        arg.Item1
                    )
            );
        }

        public static Gen<GithubAuth> GithubAuth() {
            return Arb.Generate<Tuple<string>>().Select(
                arg =>
                    new GithubAuth(
                        arg.Item1,
                        arg.Item1
                    )
            );
        }

        public static Gen<GithubIssueSearch> GithubIssueSearch() {
            return Arb.Generate<Tuple<List<GithubIssueSearchMatch>, string, string?, GithubIssueState?>>().Select(
                arg =>
                    new GithubIssueSearch(
                        arg.Item1,
                        arg.Item2,
                        arg.Item3,
                        arg.Item4
                    )
            );
        }

        public static Gen<GithubIssuesTemplate> GithubIssuesTemplate() {
            return Arb.Generate<Tuple<SecretData<GithubAuth>, NonEmptyString, GithubIssueSearch, List<string>, GithubIssueDuplicate>>().Select(
                arg =>
                    new GithubIssuesTemplate(
                        arg.Item1,
                        arg.Item2.Item,
                        arg.Item2.Item,
                        arg.Item2.Item,
                        arg.Item2.Item,
                        arg.Item3,
                        arg.Item4,
                        arg.Item4,
                        arg.Item5
                    )
            );
        }

        public static Gen<NotificationTemplate> NotificationTemplate() {
            return Gen.OneOf(new[] {
                AdoTemplate().Select(a => a as NotificationTemplate),
                TeamsTemplate().Select(e => e as NotificationTemplate),
                GithubIssuesTemplate().Select(e => e as NotificationTemplate)
            });
        }

        public static Gen<Notification> Notification() {
            return Arb.Generate<Tuple<Container, Guid, NotificationTemplate>>().Select(
                arg => new Notification(
                    Container: arg.Item1,
                    NotificationId: arg.Item2,
                    Config: arg.Item3
                )
            );
        }

        public static Gen<Job> Job() {
            return Arb.Generate<Tuple<Guid, JobState, JobConfig, string?, DateTimeOffset?, List<JobTaskInfo>?, UserInfo>>().Select(
                arg => new Job(
                    JobId: arg.Item1,
                    State: arg.Item2,
                    Config: arg.Item3,
                    Error: arg.Item4,
                    EndTime: arg.Item5
                )
            );
        }

        public static Gen<DownloadableEventMessage> DownloadableEventMessage() {
            return Arb.Generate<Tuple<Guid, BaseEvent, Guid, string, DateTime, Uri>>().Select(
                arg =>
                    new DownloadableEventMessage(
                        EventId: arg.Item1,
                        EventType: arg.Item2.GetEventType(),
                        Event: arg.Item2,
                        InstanceId: arg.Item3,
                        InstanceName: arg.Item4,
                        CreatedAt: arg.Item5,
                        SasUrl: arg.Item6
                    )
            );
        }
    }

    public class OrmArb {

        public static Arbitrary<PoolName> PoolName { get; } = OrmGenerators.PoolNameGen.ToArbitrary();
        public static Arbitrary<ScalesetId> ScalesetId { get; } = OrmGenerators.ScalesetIdGen.ToArbitrary();

        public static Arbitrary<IReadOnlyList<T>> ReadOnlyList<T>()
            => Arb.Default.List<T>().Convert(x => (IReadOnlyList<T>)x, x => (List<T>)x);

        public static Arbitrary<Version> Version() {
            return Arb.From(OrmGenerators.Version());
        }

        public static Arbitrary<Uri> Uri() {
            return Arb.From(OrmGenerators.Uri());
        }

        public static Arbitrary<BaseEvent> BaseEvent() {
            return Arb.From(OrmGenerators.BaseEvent());
        }

        public static Arbitrary<NodeTasks> NodeTasks() {
            return Arb.From(OrmGenerators.NodeTasks());
        }

        public static Arbitrary<Node> Node() {
            return Arb.From(OrmGenerators.Node);
        }

        public static Arbitrary<ProxyForward> ProxyForward() {
            return Arb.From(OrmGenerators.ProxyForward);
        }

        public static Arbitrary<Proxy> Proxy() {
            return Arb.From(OrmGenerators.Proxy);
        }

        public static Arbitrary<EventMessage> EventMessage() {
            return Arb.From(OrmGenerators.EventMessage());
        }

        public static Arbitrary<DownloadableEventMessage> DownloadableEventMessage() {
            return Arb.From(OrmGenerators.DownloadableEventMessage());
        }

        public static Arbitrary<NetworkConfig> NetworkConfig() {
            return Arb.From(OrmGenerators.NetworkConfig());
        }

        public static Arbitrary<NetworkSecurityGroupConfig> NetworkSecurityConfig() {
            return Arb.From(OrmGenerators.NetworkSecurityGroupConfig());
        }

        public static Arbitrary<InstanceConfig> InstanceConfig() {
            return Arb.From(OrmGenerators.InstanceConfig());
        }

        public static Arbitrary<WebhookMessageLog> WebhookMessageLog() {
            return Arb.From(OrmGenerators.WebhookMessageLog());
        }

        public static Arbitrary<Task> Task() {
            return Arb.From(OrmGenerators.Task());
        }

        public static Arbitrary<ImageReference> ImageReference()
            => Arb.From(OrmGenerators.ImageReferenceGen);

        public static Arbitrary<Scaleset> Scaleset()
            => Arb.From(OrmGenerators.Scaleset);

        public static Arbitrary<Webhook> Webhook() {
            return Arb.From(OrmGenerators.Webhook());
        }

        public static Arbitrary<WebhookMessage> WebhookMessage() {
            return Arb.From(OrmGenerators.WebhookMessage());
        }

        public static Arbitrary<Report> Report() {
            return Arb.From(OrmGenerators.Report());
        }

        public static Arbitrary<Container> Container() {
            return Arb.From(OrmGenerators.ContainerGen);
        }

        public static Arbitrary<Region> Region() {
            return Arb.From(OrmGenerators.RegionGen);
        }

        public static Arbitrary<NotificationTemplate> NotificationTemplate() {
            return Arb.From(OrmGenerators.NotificationTemplate());
        }

        public static Arbitrary<Notification> Notification() {
            return Arb.From(OrmGenerators.Notification());
        }


        public static Arbitrary<WebhookMessageEventGrid> WebhookMessageEventGrid() {
            return Arb.From(OrmGenerators.WebhookMessageEventGrid());
        }
        public static Arbitrary<Job> Job() {
            return Arb.From(OrmGenerators.Job());
        }

        public static Arbitrary<ISecret<T>> ISecret<T>() {
            return Arb.From(OrmGenerators.ISecret<T>());
        }


    }


    public static class EqualityComparison {
        private static HashSet<Type> _baseTypes = new HashSet<Type>(
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
        static bool IEnumerableEqual<T>(IEnumerable<T>? a, IEnumerable<T>? b) {
            if (a is null) {
                return b is null;
            }

            if (b is null) {
                return false;
            }

            if (a.Count() != b.Count()) {
                return false;
            }

            foreach (var (first, second) in a.Zip(b)) {
                if (!AreEqual(first, second)) {
                    return false;
                }
            }

            return true;
        }

        static bool IDictionaryEqual<TKey, TValue>(IDictionary<TKey, TValue>? a, IDictionary<TKey, TValue>? b, Func<TValue, TValue, bool> cmp) {
            if (a is null && b is null)
                return true;

            if (a!.Count == 0 && b!.Count == 0)
                return true;

            if (a!.Count != b!.Count)
                return false;

            return a!.Any(v => cmp(v.Value, b[v.Key]));
        }

        static bool IDictionaryEqual<TKey, TValue>(IDictionary<TKey, TValue>? a, IDictionary<TKey, TValue>? b) {
            if (a is null && b is null)
                return true;

            if (a!.Count == 0 && b!.Count == 0)
                return true;

            if (a!.Count != b!.Count)
                return false;

            return a!.Any(v => AreEqual(v.Value, b[v.Key]));
        }


        public static bool AreEqual<T>(T r1, T r2) {
            var t = typeof(T);

            if (r1 is null && r2 is null)
                return true;

            if (_baseTypes.Contains(t))
                return r1!.Equals(r2);

            foreach (var p in t.GetProperties()) {
                var v1 = p.GetValue(r1);
                var v2 = p.GetValue(r2);
                var tt = p.PropertyType;

                if (v1 is null && v2 is null)
                    continue;

                if (v1 is null || v2 is null)
                    return false;

                if (_baseTypes.Contains(tt) && !v1!.Equals(v2))
                    return false;

                if (tt.GetInterface("IEnumerable") is not null) {
                    if (!IEnumerableEqual(v1 as IEnumerable<Object>, v2 as IEnumerable<Object>))
                        return false;
                }

                if (tt.GetInterface("IDictionary") is not null) {
                    if (!IDictionaryEqual(v1 as IDictionary<Object, Object>, v2 as IDictionary<Object, Object>))
                        return false;
                }
            }
            return true;
        }
    }

    public class OrmModelsTest {
        EntityConverter _converter = new EntityConverter(new TestSecretOperations());
        ITestOutputHelper _output;

        public OrmModelsTest(ITestOutputHelper output) {
            _ = Arb.Register<OrmArb>();
            _output = output;
        }

        bool Test<T>(T e) where T : EntityBase {
            var v = _converter.ToTableEntity(e).Result;
            var r = _converter.ToRecord<T>(v);
            return EqualityComparison.AreEqual(e, r);

        }

        [Property]
        public bool Node(Node node) {
            return Test(node);
        }

        [Property]
        public bool ProxyForward(ProxyForward proxyForward) {
            return Test(proxyForward);
        }

        [Property]
        public bool Proxy(Proxy proxy) {
            return Test(proxy);
        }

        [Property]
        public bool Task(Task task) {
            return Test(task);
        }


        [Property]
        public bool InstanceConfig(InstanceConfig cfg) {
            return Test(cfg);
        }

        [Property]
        public bool Scaleset(Scaleset ss) {
            return Test(ss);
        }

        [Property]
        public bool WebhookMessageLog(WebhookMessageLog log) {
            return Test(log);
        }

        [Property]
        public bool Webhook(Webhook wh) {
            return Test(wh);
        }

        [Property]
        public bool Notification(Notification n) {
            return Test(n);
        }

        [Property]
        public bool Job(Job j) {
            return Test(j);
        }

        /*
        //Sample function on how repro a failing test run, using Replay
        //functionality of FsCheck. Feel free to
        [Property]
        void Replay()
        {
            var seed = FsCheck.Random.StdGen.NewStdGen(610100457,297085446);
            var p = Prop.ForAll((InstanceConfig x) => InstanceConfig(x) );
            p.Check(new Configuration { Replay = seed });
        }
        */
    }


    public class OrmJsonSerialization {

        JsonSerializerOptions _opts = EntityConverter.GetJsonSerializerOptions();
        ITestOutputHelper _output;

        public OrmJsonSerialization(ITestOutputHelper output) {
            _ = Arb.Register<OrmArb>();
            _output = output;
        }


        string serialize<T>(T x) {
            return JsonSerializer.Serialize(x, _opts);
        }

        T? deserialize<T>(string json) {
            return JsonSerializer.Deserialize<T>(json, _opts);
        }


        bool Test<T>(T v) {
            var j = serialize(v);
            var r = deserialize<T>(j);
            return EqualityComparison.AreEqual(v, r);
        }

        [Property]
        public bool Node(Node node) {
            return Test(node);
        }

        [Property]
        public bool ProxyForward(ProxyForward proxyForward) {
            return Test(proxyForward);
        }

        [Property]
        public bool Proxy(Proxy proxy) {
            return Test(proxy);
        }


        [Property]
        public bool Task(Task task) {
            return Test(task);
        }


        [Property]
        public bool InstanceConfig(InstanceConfig cfg) {
            return Test(cfg);
        }


        [Property]
        public bool Scaleset(Scaleset ss) {
            return Test(ss);
        }

        [Property]
        public bool WebhookMessageLog(WebhookMessageLog log) {
            return Test(log);
        }

        [Property]
        public bool Webhook(Webhook wh) {
            return Test(wh);
        }

        [Property]
        public bool WebhookMessageEventGrid(WebhookMessageEventGrid evt) {
            return Test(evt);
        }


        [Property]
        public bool WebhookMessage(WebhookMessage msg) {
            return Test(msg);
        }


        [Property]
        public bool TaskHeartbeatEntry(TaskHeartbeatEntry e) {
            return Test(e);
        }

        [Property]
        public bool NodeCommand(NodeCommand e) {
            return Test(e);
        }

        [Property]
        public bool NodeTasks(NodeTasks e) {
            return Test(e);
        }

        [Property]
        public bool ProxyHeartbeat(ProxyHeartbeat e) {
            return Test(e);
        }

        [Property]
        public bool ProxyConfig(ProxyConfig e) {
            return Test(e);
        }

        [Property]
        public bool TaskDetails(TaskDetails e) {
            return Test(e);
        }

        [Property]
        public bool TaskVm(TaskVm e) {
            return Test(e);
        }

        [Property]
        public bool TaskPool(TaskPool e) {
            return Test(e);
        }

        [Property]
        public bool TaskContainers(TaskContainers e) {
            return Test(e);
        }

        [Property]
        public bool TaskConfig(TaskConfig e) {
            return Test(e);
        }

        [Property]
        public bool TaskEventSummary(TaskEventSummary e) {
            return Test(e);
        }

        [Property]
        public bool NodeAssignment(NodeAssignment e) {
            return Test(e);
        }

        [Property]
        public bool KeyvaultExtensionConfig(KeyvaultExtensionConfig e) {
            return Test(e);
        }

        [Property]
        public bool AzureMonitorExtensionConfig(AzureMonitorExtensionConfig e) {
            return Test(e);
        }

        [Property]
        public bool AzureVmExtensionConfig(AzureVmExtensionConfig e) {
            return Test(e);
        }

        [Property]
        public bool NetworkConfig(NetworkConfig e) {
            return Test(e);
        }

        [Property]
        public bool NetworkSecurityGroupConfig(NetworkSecurityGroupConfig e) {
            return Test(e);
        }

        [Property]
        public bool Report(Report e) {
            return Test(e);
        }

        [Property]
        public bool Notification(Notification e) {
            return Test(e);
        }

        [Property]
        public bool NoReproReport(NoReproReport e) {
            return Test(e);
        }

        [Property]
        public bool CrashTestResult(CrashTestResult e) {
            return Test(e);
        }

        [Property]
        public bool NotificationTemplate(NotificationTemplate e) {
            return Test(e);
        }


        [Property]
        public bool RegressionReport(RegressionReport e) {
            return Test(e);
        }

        [Property]
        public bool Job(Job e) {
            return Test(e);
        }


        [Property]
        public bool EventNodeHeartbeat(EventNodeHeartbeat e) {
            return Test(e);
        }


        [Property]
        public bool EventTaskHeartbeat(EventTaskHeartbeat e) {
            return Test(e);
        }

        [Property]
        public bool EventTaskStopped(EventTaskStopped e) {
            return Test(e);
        }

        [Property]
        public bool EventInstanceConfigUpdated(EventInstanceConfigUpdated e) {
            return Test(e);
        }

        [Property]
        public bool EventProxyCreated(EventProxyCreated e) {
            return Test(e);
        }

        [Property]
        public bool EventProxyDeleted(EventProxyDeleted e) {
            return Test(e);
        }

        [Property]
        public bool EventProxyFailed(EventProxyFailed e) {
            return Test(e);
        }

        [Property]
        public bool EventProxyStateUpdated(EventProxyStateUpdated e) {
            return Test(e);
        }


        [Property]
        public bool EventCrashReported(EventCrashReported e) {
            return Test(e);
        }


        [Property]
        public bool EventRegressionReported(EventRegressionReported e) {
            return Test(e);
        }


        [Property]
        public bool EventFileAdded(EventFileAdded e) {
            return Test(e);
        }

        [Property]
        public bool EventTaskFailed(EventTaskFailed e) {
            return Test(e);
        }

        [Property]
        public bool EventTaskStateUpdated(EventTaskStateUpdated e) {
            return Test(e);
        }

        [Property]
        public bool EventScalesetFailed(EventScalesetFailed e) {
            return Test(e);
        }


        [Property]
        public bool EventScalesetResizeScheduled(EventScalesetResizeScheduled e) {
            return Test(e);
        }


        [Property]
        public bool EventScalesetStateUpdated(EventScalesetStateUpdated e) {
            return Test(e);
        }

        [Property]
        public bool EventNodeDeleted(EventNodeDeleted e) {
            return Test(e);
        }

        [Property]
        public bool EventNodeCreated(EventNodeCreated e) {
            return Test(e);
        }

        [Property]
        public bool EventMessage(DownloadableEventMessage e) {
            return Test(e);
        }

        [Property]
        public bool Error(Error e) {
            return Test(e);
        }

        [Property]
        public bool Container(Container c) {
            return Test(c);
        }



        //Sample function on how repro a failing test run, using Replay
        //functionality of FsCheck. Feel free to
        [Property]
        void Replay() {
            var seed = FsCheck.Random.StdGen.NewStdGen(811038773, 297085737);
            var p = Prop.ForAll((NotificationTemplate x) => NotificationTemplate(x));
            p.Check(new Configuration { Replay = seed });
        }
    }

}
