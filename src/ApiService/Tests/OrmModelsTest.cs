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
    public class Arbitraries {

        public static Arbitrary<PoolName> ArbPoolName()
            => Arb.From(from name in Arb.Generate<NonEmptyString>()
                        where PoolName.IsValid(name.Get)
                        select PoolName.Parse(name.Get));

        public static Arbitrary<ScalesetId> ArbScalesetId()
            => Arb.From(from name in Arb.Generate<NonEmptyString>()
                        where ScalesetId.IsValid(name.Get)
                        select ScalesetId.Parse(name.Get));

        public static Arbitrary<IReadOnlyList<T>> ReadOnlyList<T>()
            => Arb.Default.List<T>().Convert(x => (IReadOnlyList<T>)x, x => (List<T>)x);

        public static Arbitrary<Version> ArbVersion()
            //OneFuzz version uses 3 number version
            => Arb.From(from v in Arb.Generate<(UInt16, UInt16, UInt16)>()
                        select new Version(v.Item1, v.Item2, v.Item3));

        public static Arbitrary<Uri> ArbUri()
            => Arb.From(from address in Arb.Generate<IPv4Address>()
                        select new Uri($"https://{address.Item}:8080/"));

        public static Arbitrary<BaseEvent> ArbBaseEvent()
            => Arb.From(Gen.OneOf(new[] {
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
            }));
        public static Arbitrary<DateOnly> ArbDateOnly()
            => Arb.From(from date in Arb.Generate<DateTime>()
                        select DateOnly.FromDateTime(date));

        public static Arbitrary<DownloadableEventMessage> ArbDownloadableEventMessage()
            => Arb.From(from eventId in Arb.Generate<Guid>()
                        from ev in Arb.Generate<BaseEvent>()
                        from instanceId in Arb.Generate<Guid>()
                        from instanceName in Arb.Generate<string>()
                        from createdAt in Arb.Generate<DateTime>()
                        from sasUrl in Arb.Generate<Uri>()
                        from expiresOn in Arb.Generate<DateOnly?>()
                        select new DownloadableEventMessage(
                            EventId: eventId,
                            EventType: ev.GetEventType(),
                            Event: ev,
                            InstanceId: instanceId,
                            InstanceName: instanceName,
                            CreatedAt: createdAt,
                            SasUrl: sasUrl,
                            ExpiresOn: expiresOn));

        public static Arbitrary<EventMessage> ArbEventMessage()
            => Arb.From(from eventId in Arb.Generate<Guid>()
                        from ev in Arb.Generate<BaseEvent>()
                        from instanceId in Arb.Generate<Guid>()
                        from instanceName in Arb.Generate<string>()
                        from createdAt in Arb.Generate<DateTime>()
                        select new EventMessage(
                            EventId: eventId,
                            EventType: ev.GetEventType(),
                            Event: ev,
                            InstanceId: instanceId,
                            InstanceName: instanceName,
                            CreatedAt: createdAt));

        public static Arbitrary<NetworkConfig> ArbNetworkConfig()
            => Arb.From(from addressSpace in Arb.Generate<IPv4Address>()
                        from subnet in Arb.Generate<IPv4Address>()
                        select new NetworkConfig(
                            AddressSpace: addressSpace.Item.ToString(),
                            Subnet: subnet.Item.ToString()));

        public static Arbitrary<NetworkSecurityGroupConfig> ArbNetworkSecurityConfig()
            => Arb.From(from tags in Arb.Generate<string[]>()
                        from ips in Arb.Generate<IPv4Address[]>()
                        select
                    new NetworkSecurityGroupConfig(
                        AllowedServiceTags: tags,
                        AllowedIps: ips.Select(ip => ip.Item.ToString()).ToArray()));

        public static Arbitrary<InstanceConfig> ArbInstanceConfig()
            => Arb.From(from instanceName in Arb.Generate<string>()
                        from admins in Arb.Generate<Guid[]?>()
                        from allowedAadTenants in Arb.Generate<string[]>()
                        from networkConfig in Arb.Generate<NetworkConfig>()
                        from proxyNsgConfig in Arb.Generate<NetworkSecurityGroupConfig>()
                        from extensions in Arb.Generate<AzureVmExtensionConfig?>()
                        from proxyVmSku in Arb.Generate<NonNull<string>>()
                        from requireAdminPrivileges in Arb.Generate<bool>()
                        from apiAccessRules in Arb.Generate<IDictionary<string, ApiAccessRule>?>()
                        from groupMembership in Arb.Generate<IDictionary<Guid, Guid[]>?>()
                        from vmTags in Arb.Generate<IDictionary<string, string>?>()
                        from vmssTags in Arb.Generate<IDictionary<string, string>?>()
                        select new InstanceConfig(
                            InstanceName: instanceName,
                            Admins: admins,
                            AllowedAadTenants: allowedAadTenants,
                            NetworkConfig: networkConfig,
                            ProxyNsgConfig: proxyNsgConfig,
                            Extensions: extensions,
                            ProxyVmSku: proxyVmSku.Get,
                            RequireAdminPrivileges: requireAdminPrivileges,
                            ApiAccessRules: apiAccessRules,
                            GroupMembership: groupMembership,
                            VmTags: vmTags,
                            VmssTags: vmssTags));

        public static Arbitrary<WebhookMessageLog> WebhookMessageLog()
            => Arb.From(from id in Arb.Generate<Guid>()
                        from ev in Arb.Generate<BaseEvent>()
                        from instanceId in Arb.Generate<Guid>()
                        from instanceName in Arb.Generate<string>()
                        from webhookId in Arb.Generate<Guid>()
                        from state in Arb.Generate<WebhookMessageState>()
                        from tryCount in Arb.Generate<long>()
                        select new WebhookMessageLog(
                            EventId: id,
                            EventType: ev.GetEventType(),
                            Event: ev,
                            InstanceId: instanceId,
                            InstanceName: instanceName,
                            WebhookId: webhookId,
                            State: state,
                            TryCount: tryCount));

        public static Arbitrary<ImageReference> ArbImageReference()
            => Arb.From(Gen.Elements(
                ImageReference.MustParse("Canonical:UbuntuServer:20.04-LTS:latest"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName"),
                ImageReference.MustParse($"/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/images/imageName")));

        public static Arbitrary<WebhookMessage> WebhookMessage()
            => Arb.From(from id in Arb.Generate<Guid>()
                        from ev in Arb.Generate<BaseEvent>()
                        from instanceId in Arb.Generate<Guid>()
                        from instanceName in Arb.Generate<string>()
                        from webhookId in Arb.Generate<Guid>()
                        from createdAt in Arb.Generate<DateTime>()
                        from sasUrl in Arb.Generate<Uri>()
                        select new WebhookMessage(
                            EventId: id,
                            EventType: ev.GetEventType(),
                            Event: ev,
                            InstanceId: instanceId,
                            InstanceName: instanceName,
                            WebhookId: webhookId,
                            CreatedAt: createdAt,
                            SasUrl: sasUrl));

        public static Arbitrary<Report> Report()
            => Arb.From(from s in Arb.Generate<string>()
                        from b in Arb.Generate<BlobRef>()
                        from slist in Arb.Generate<List<string>>()
                        from g in Arb.Generate<Guid>()
                        from i in Arb.Generate<int>()
                        from u in Arb.Generate<Uri?>()
                        select new Report(
                            InputUrl: s,
                            InputBlob: b,
                            Executable: s,
                            CrashType: s,
                            CrashSite: s,
                            CallStack: slist,
                            CallStackSha256: s,
                            InputSha256: s,
                            AsanLog: s,
                            TaskId: g,
                            JobId: g,
                            ScarinessScore: i,
                            ScarinessDescription: s,
                            MinimizedStack: slist,
                            MinimizedStackSha256: s,
                            MinimizedStackFunctionNames: slist,
                            MinimizedStackFunctionNamesSha256: s,
                            MinimizedStackFunctionLines: slist,
                            MinimizedStackFunctionLinesSha256: s,
                            ToolName: s,
                            ToolVersion: s,
                            OnefuzzVersion: s,
                            ReportUrl: u));

        public static Arbitrary<Container> ArbContainer()
            => Arb.From(from len in Gen.Choose(3, 63)
                        from name in Gen.ArrayOf(len, Gen.Elements<char>("abcdefghijklmnopqrstuvwxyz0123456789-"))
                        let nameString = new string(name)
                        where Container.IsValid(nameString)
                        select Container.Parse(nameString));

        public static Arbitrary<Region> ArbRegion()
            => Arb.From(from name in Arb.Generate<NonEmptyString>()
                        where Region.IsValid(name.Get)
                        select Region.Parse(name.Get));

        public static Arbitrary<NotificationTemplate> ArbNotificationTemplate()
            => Arb.From(
                Gen.OneOf(new[] {
                    Arb.Generate<AdoTemplate>().Select(a => a as NotificationTemplate),
                    Arb.Generate<TeamsTemplate>().Select(e => e as NotificationTemplate),
                    Arb.Generate<GithubIssuesTemplate>().Select(e => e as NotificationTemplate)
                }));

        public static Arbitrary<AdoTemplate> ArbAdoTemplate()
            => Arb.From(from baseUrl in Arb.Generate<Uri>()
                        from authToken in Arb.Generate<SecretData<string>>()
                        from str in Arb.Generate<NonEmptyString>()
                        from fields in Arb.Generate<List<string>>()
                        from adoFields in Arb.Generate<Dictionary<string, string>>()
                        from dupeTemplate in Arb.Generate<ADODuplicateTemplate>()
                        select new AdoTemplate(
                            baseUrl,
                            authToken,
                            str.Get,
                            str.Get,
                            fields,
                            adoFields,
                            dupeTemplate,
                            str.Get));

        public static Arbitrary<TeamsTemplate> ArbTeamsTemplate()
            => Arb.From(from data in Arb.Generate<SecretData<string>>()
                        select new TeamsTemplate(data));

        public static Arbitrary<GithubIssuesTemplate> ArbGithubIssuesTemplate()
            => Arb.From(from data in Arb.Generate<SecretData<GithubAuth>>()
                        from str in Arb.Generate<NonEmptyString>()
                        from search in Arb.Generate<GithubIssueSearch>()
                        from assignees in Arb.Generate<List<string>>()
                        from labels in Arb.Generate<List<string>>()
                        from dupe in Arb.Generate<GithubIssueDuplicate>()
                        select new GithubIssuesTemplate(
                            data,
                            str.Get, str.Get, str.Get, str.Get,
                            search,
                            assignees,
                            labels,
                            dupe));

        public static Arbitrary<WebhookMessageEventGrid> ArbWebhookMessageEventGrid()
            => Arb.From(from version in Arb.Generate<string>()
                        from subject in Arb.Generate<string>()
                        from id in Arb.Generate<Guid>()
                        from eventTime in Arb.Generate<DateTimeOffset>()
                        from message in Arb.Generate<WebhookMessage>()
                        select new WebhookMessageEventGrid(
                                DataVersion: version,
                                Subject: subject,
                                EventType: message.EventType,
                                Data: message,
                                Id: id,
                                EventTime: eventTime));

        public static Arbitrary<ISecret<T>> ISecret<T>()
            => Arb.From(Gen.Constant<ISecret<T>>(new SecretAddress<T>(new Uri("http://example.com"))));
    }

    public class OrmModelsTest {
        private readonly JsonSerializerOptions _opts = EntityConverter.GetJsonSerializerOptions();
        private readonly EntityConverter _converter = new(new TestSecretOperations());

        public OrmModelsTest() {
            _ = Arb.Register<Arbitraries>();
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
            _ = Arb.Register<Arbitraries>();
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
