using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;

namespace Tests {

    public class OrmGenerators {
        public static Gen<BaseEvent> BaseEvent()
            => Gen.OneOf(new[] {
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

        public static Gen<Uri> Uri { get; }
            = from address in Arb.Generate<IPv4Address>()
              select new Uri($"https://{address.Item}:8080/");

        public static Gen<ISecret<T>> ISecret<T>()
            => Gen.Constant<ISecret<T>>(new SecretAddress<T>(new Uri("http://example.com")));

        public static Gen<Version> Version { get; }
            //OneFuzz version uses 3 number version
            = from v in Arb.Generate<(UInt16, UInt16, UInt16)>()
              select new Version(v.Item1, v.Item2, v.Item3);

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

        public static Gen<PoolName> PoolNameGen { get; }
            = from name in Arb.Generate<NonEmptyString>()
              where PoolName.IsValid(name.Get)
              select PoolName.Parse(name.Get);

        public static Gen<ScalesetId> ScalesetIdGen()
            => from name in Arb.Generate<NonEmptyString>()
               where ScalesetId.IsValid(name.Get)
               select ScalesetId.Parse(name.Get);

        public static Gen<Region> RegionGen { get; }
            = from name in Arb.Generate<NonEmptyString>()
              where Region.IsValid(name.Get)
              select Region.Parse(name.Get);

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

        public static Gen<ImageReference> ImageReferenceGen { get; } =
            Gen.Elements(
                ImageReference.MustParse("Canonical:UbuntuServer:20.04-LTS:latest"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/images/imageName"));


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
            var gen1 = Arb.Generate<Tuple<string, string, BaseEvent, Guid, DateTimeOffset, Uri>>();
            var gen2 = WebhookMessage();
            return gen1.Zip(gen2)

            .Select(
                arg =>
                    new WebhookMessageEventGrid(
                        DataVersion: arg.Item1.Item1,
                        Subject: arg.Item1.Item2,
                        EventType: arg.Item1.Item3.GetEventType(),
                        Data: arg.Item2,
                        Id: arg.Item1.Item4,
                        EventTime: arg.Item1.Item5
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

        public static Gen<DownloadableEventMessage> DownloadableEventMessage() {
            return Arb.Generate<Tuple<Guid, BaseEvent, Guid, string, DateTime, Uri, DateOnly?>>().Select(
                arg =>
                    new DownloadableEventMessage(
                        EventId: arg.Item1,
                        EventType: arg.Item2.GetEventType(),
                        Event: arg.Item2,
                        InstanceId: arg.Item3,
                        InstanceName: arg.Item4,
                        CreatedAt: arg.Item5,
                        SasUrl: arg.Item6,
                        ExpiresOn: arg.Item7
                    )
            );
        }

        public static Gen<DateOnly> DateOnly() {
            return Arb.Generate<Tuple<DateTime>>().Select(
                arg =>
                    System.DateOnly.FromDateTime(arg.Item1)
            );
        }
    }

    public class OrmArb {

        public static Arbitrary<PoolName> PoolName { get; } = OrmGenerators.PoolNameGen.ToArbitrary();
        public static Arbitrary<ScalesetId> ScalesetId { get; } = OrmGenerators.ScalesetIdGen().ToArbitrary();

        public static Arbitrary<IReadOnlyList<T>> ReadOnlyList<T>()
            => Arb.Default.List<T>().Convert(x => (IReadOnlyList<T>)x, x => (List<T>)x);

        public static Arbitrary<Version> Version { get; } = OrmGenerators.Version.ToArbitrary();

        public static Arbitrary<Uri> Uri { get; } = OrmGenerators.Uri.ToArbitrary();

        public static Arbitrary<BaseEvent> BaseEvent { get; } = OrmGenerators.BaseEvent().ToArbitrary();

        public static Arbitrary<NodeTasks> NodeTasks { get; } = Arb.Default.Derive<NodeTasks>();

        public static Arbitrary<Node> Node { get; } = Arb.Default.Derive<Node>();

        public static Arbitrary<ProxyForward> ProxyForward { get; } = Arb.Default.Derive<ProxyForward>();

        public static Arbitrary<Proxy> Proxy { get; } = Arb.Default.Derive<Proxy>();

        public static Arbitrary<EventMessage> EventMessage() {
            return Arb.From(OrmGenerators.EventMessage());
        }

        public static Arbitrary<DateOnly> DateOnly() {
            return Arb.From(OrmGenerators.DateOnly());
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

        public static Arbitrary<Task> Task { get; } = Arb.Default.Derive<Task>();

        public static Arbitrary<ImageReference> ImageReference() => OrmGenerators.ImageReferenceGen.ToArbitrary();

        public static Arbitrary<Scaleset> Scaleset { get; } = Arb.Default.Derive<Scaleset>();

        public static Arbitrary<Webhook> Webhook { get; } = Arb.Default.Derive<Webhook>();

        public static Arbitrary<WebhookMessage> WebhookMessage() => OrmGenerators.WebhookMessage().ToArbitrary();

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

        public static Arbitrary<Job> Job() => Arb.Default.Derive<Job>();

        public static Arbitrary<ISecret<T>> ISecret<T>() {
            return Arb.From(OrmGenerators.ISecret<T>());
        }
    }

    public class OrmModelsTest {
        private readonly JsonSerializerOptions _opts = EntityConverter.GetJsonSerializerOptions();
        private readonly EntityConverter _converter = new(new TestSecretOperations());

        public OrmModelsTest() {
            _ = Arb.Register<OrmArb>();
        }

        private void Test<T>(T e) where T : EntityBase {
            var r = _converter.ToRecord<T>(_converter.ToTableEntity(e).Result);
            // cheap way to compare objects:
            var s1 = JsonSerializer.Serialize(e, _opts);
            var s2 = JsonSerializer.Serialize(r, _opts);
            Assert.Equal(s1, s2);
        }

        [Property]
        public void Node(Node node) => Test(node);

        [Property]
        public void ProxyForward(ProxyForward proxyForward) => Test(proxyForward);

        [Property]
        public void Proxy(Proxy proxy) => Test(proxy);

        [Property]
        public void Task(Task task) => Test(task);


        [Property]
        public void InstanceConfig(InstanceConfig cfg) => Test(cfg);

        [Property]
        public void Scaleset(Scaleset ss) => Test(ss);

        [Property]
        public void WebhookMessageLog(WebhookMessageLog log) => Test(log);

        [Property]
        public void Webhook(Webhook wh) => Test(wh);

        [Property]
        public void Notification(Notification n) => Test(n);

        [Property]
        public void Job(Job j) => Test(j);

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
        private readonly JsonSerializerOptions _opts = EntityConverter.GetJsonSerializerOptions();

        public OrmJsonSerialization() {
            _ = Arb.Register<OrmArb>();
        }

        void Test<T>(T v) {
            var s1 = JsonSerializer.Serialize(v, _opts);
            var s2 = JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(s1, _opts), _opts);
            Assert.Equal(s1, s2);
        }

        [Property]
        public void Node(Node node) => Test(node);

        [Property]
        public void ProxyForward(ProxyForward proxyForward) => Test(proxyForward);

        [Property]
        public void Proxy(Proxy proxy) => Test(proxy);


        [Property]
        public void Task(Task task) => Test(task);

        [Property]
        public void InstanceConfig(InstanceConfig cfg) => Test(cfg);

        [Property]
        public void Scaleset(Scaleset ss) => Test(ss);

        [Property]
        public void WebhookMessageLog(WebhookMessageLog log) => Test(log);

        [Property]
        public void Webhook(Webhook wh) => Test(wh);

        [Property]
        public void WebhookMessageEventGrid(WebhookMessageEventGrid evt) => Test(evt);

        [Property]
        public void WebhookMessage(WebhookMessage msg) => Test(msg);

        [Property]
        public void TaskHeartbeatEntry(TaskHeartbeatEntry e) => Test(e);

        [Property]
        public void NodeCommand(NodeCommand e) => Test(e);

        [Property]
        public void NodeTasks(NodeTasks e) => Test(e);

        [Property]
        public void ProxyHeartbeat(ProxyHeartbeat e) => Test(e);

        [Property]
        public void ProxyConfig(ProxyConfig e) => Test(e);

        [Property]
        public void TaskDetails(TaskDetails e) => Test(e);

        [Property]
        public void TaskVm(TaskVm e) => Test(e);

        [Property]
        public void TaskPool(TaskPool e) => Test(e);

        [Property]
        public void TaskContainers(TaskContainers e) => Test(e);

        [Property]
        public void TaskConfig(TaskConfig e) => Test(e);

        [Property]
        public void TaskEventSummary(TaskEventSummary e) => Test(e);

        [Property]
        public void NodeAssignment(NodeAssignment e) => Test(e);

        [Property]
        public void KeyvaultExtensionConfig(KeyvaultExtensionConfig e) => Test(e);

        [Property]
        public void AzureMonitorExtensionConfig(AzureMonitorExtensionConfig e) => Test(e);

        [Property]
        public void AzureVmExtensionConfig(AzureVmExtensionConfig e) => Test(e);

        [Property]
        public void NetworkConfig(NetworkConfig e) => Test(e);

        [Property]
        public void NetworkSecurityGroupConfig(NetworkSecurityGroupConfig e) => Test(e);

        [Property]
        public void Report(Report e) => Test(e);

        [Property]
        public void Notification(Notification e) => Test(e);

        [Property]
        public void NoReproReport(NoReproReport e) => Test(e);

        [Property]
        public void CrashTestResult(CrashTestResult e) => Test(e);

        [Property]
        public void NotificationTemplate(NotificationTemplate e) => Test(e);


        [Property]
        public void RegressionReport(RegressionReport e) => Test(e);

        [Property]
        public void Job(Job e) => Test(e);


        [Property]
        public void EventNodeHeartbeat(EventNodeHeartbeat e) => Test(e);


        [Property]
        public void EventTaskHeartbeat(EventTaskHeartbeat e) => Test(e);

        [Property]
        public void EventTaskStopped(EventTaskStopped e) => Test(e);

        [Property]
        public void EventInstanceConfigUpdated(EventInstanceConfigUpdated e) => Test(e);

        [Property]
        public void EventProxyCreated(EventProxyCreated e) => Test(e);

        [Property]
        public void EventProxyDeleted(EventProxyDeleted e) => Test(e);

        [Property]
        public void EventProxyFailed(EventProxyFailed e) => Test(e);

        [Property]
        public void EventProxyStateUpdated(EventProxyStateUpdated e) => Test(e);


        [Property]
        public void EventCrashReported(EventCrashReported e) => Test(e);


        [Property]
        public void EventRegressionReported(EventRegressionReported e) => Test(e);


        [Property]
        public void EventFileAdded(EventFileAdded e) => Test(e);

        [Property]
        public void EventTaskFailed(EventTaskFailed e) => Test(e);

        [Property]
        public void EventTaskStateUpdated(EventTaskStateUpdated e) => Test(e);

        [Property]
        public void EventScalesetFailed(EventScalesetFailed e) => Test(e);

        [Property]
        public void EventScalesetResizeScheduled(EventScalesetResizeScheduled e) => Test(e);

        [Property]
        public void EventScalesetStateUpdated(EventScalesetStateUpdated e) => Test(e);

        [Property]
        public void EventNodeDeleted(EventNodeDeleted e) => Test(e);

        [Property]
        public void EventNodeCreated(EventNodeCreated e) => Test(e);

        [Property]
        public void EventMessage(DownloadableEventMessage e) => Test(e);

        [Property]
        public void Error(Error e) => Test(e);

        [Property]
        public void Container(Container c) => Test(c);

        //Sample function on how repro a failing test run, using Replay
        //functionality of FsCheck. Feel free to
        /*
        void Replay() {
            var seed = FsCheck.Random.StdGen.NewStdGen(811038773, 297085737);
            var p = Prop.ForAll((NotificationTemplate x) => NotificationTemplate(x));
            p.Check(new Configuration { Replay = seed });
        }
        */
    }
}
