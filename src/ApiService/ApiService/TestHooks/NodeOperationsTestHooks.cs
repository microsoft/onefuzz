using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


#if DEBUG
namespace ApiService.TestHooks {

    record MarkTasks(Node node, Error? error);

    public class NodeOperationsTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly INodeOperations _nodeOps;

        public NodeOperationsTestHooks(ILogTracer log, IConfigOperations configOps, INodeOperations nodeOps) {
            _log = log.WithTag("TestHooks", nameof(NodeOperationsTestHooks));
            _configOps = configOps;
            _nodeOps = nodeOps;
        }

        [Function("GetByMachineIdTestHook")]
        public async Task<HttpResponseData> GetByMachineId([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "testhooks/nodeOperations/getByMachineId")] HttpRequestData req) {
            _log.Info("Get node by machine id");

            var query = UriExtension.GetQueryComponents(req.Url);
            var machineId = query["machineId"];

            var node = await _nodeOps.GetByMachineId(Guid.Parse(machineId));

            var msg = JsonSerializer.Serialize(node, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(msg);
            return resp;
        }

        [Function("CanProcessNewWorkTestHook")]
        public async Task<HttpResponseData> CanProcessNewWork([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/canProcessNewWork")] HttpRequestData req) {
            _log.Info("Can process new work");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = await _nodeOps.CanProcessNewWork(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("IsOutdatedTestHook")]
        public async Task<HttpResponseData> IsOutdated([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/isOutdated")] HttpRequestData req) {
            _log.Info("Is outdated");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.IsOutdated(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("IsTooOldTestHook")]
        public async Task<HttpResponseData> IsTooOld([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/isTooOld")] HttpRequestData req) {
            _log.Info("Is too old");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.IsTooOld(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }

        [Function("CouldShrinkScalesetTestHook")]
        public async Task<HttpResponseData> CouldShrinkScaleset([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/couldShrinkScaleset")] HttpRequestData req) {
            _log.Info("Could shrink scaleset");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.CouldShrinkScaleset(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("SetHaltTestHook")]
        public async Task<HttpResponseData> SetHalt([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/setHalt")] HttpRequestData req) {
            _log.Info("set halt");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.SetHalt(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }

        [Function("SetStateTestHook")]
        public async Task<HttpResponseData> SetState([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/setState")] HttpRequestData req) {
            _log.Info("set state");

            var query = UriExtension.GetQueryComponents(req.Url);
            var state = Enum.Parse<NodeState>(query["state"]);

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.SetState(node!, state);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("ToReimageTestHook")]
        public async Task<HttpResponseData> ToReimage([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/toReimage")] HttpRequestData req) {
            _log.Info("to reimage");

            var query = UriExtension.GetQueryComponents(req.Url);
            var done = UriExtension.GetBool("done", query, false);

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.ToReimage(node!, done);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }

        [Function("SendStopIfFreeTestHook")]
        public async Task<HttpResponseData> SendStopIfFree([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/sendStopIfFree")] HttpRequestData req) {
            _log.Info("send stop if free");

            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.SendStopIfFree(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("SearchStatesTestHook")]
        public async Task<HttpResponseData> SearchStates([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/searchStates")] HttpRequestData req) {
            _log.Info("search states");

            var query = UriExtension.GetQueryComponents(req.Url);
            Guid? poolId = UriExtension.GetGuid("poolId", query);
            Guid? scaleSetId = UriExtension.GetGuid("scaleSetId", query);

            List<NodeState>? states = default;
            if (query.ContainsKey("states")) {
                states = query["states"].Split('-').Select(s => Enum.Parse<NodeState>(s)).ToList();
            }
            string? poolNameString = UriExtension.GetString("poolName", query);

            PoolName? poolName = poolNameString is null ? null : PoolName.Parse(poolNameString);

            var excludeUpdateScheduled = UriExtension.GetBool("excludeUpdateScheduled", query, false);
            int? numResults = UriExtension.GetInt("numResults", query);
            var r = _nodeOps.SearchStates(poolId, scaleSetId, states, poolName, excludeUpdateScheduled, numResults);
            var json = JsonSerializer.Serialize(await r.ToListAsync(), EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }


        [Function("DeleteNodeTestHook")]
        public async Task<HttpResponseData> DeleteNode([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "testhooks/nodeOperations/node")] HttpRequestData req) {
            _log.Info("delete node");
            var s = await req.ReadAsStringAsync();
            var node = JsonSerializer.Deserialize<Node>(s!, EntityConverter.GetJsonSerializerOptions());

            var r = _nodeOps.Delete(node!);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }


        [Function("ReimageLongLivedNodesTestHook")]
        public async Task<HttpResponseData> ReimageLongLivedNodes([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "testhooks/nodeOperations/reimageLongLivedNodes")] HttpRequestData req) {
            _log.Info("reimage long lived nodes");
            var query = UriExtension.GetQueryComponents(req.Url);

            var r = _nodeOps.ReimageLongLivedNodes(Guid.Parse(query["scaleSetId"]));
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(r);
            return resp;
        }

        [Function("CreateTestHook")]
        public async Task<HttpResponseData> CreateNode([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "testhooks/nodeOperations/node")] HttpRequestData req) {

            _log.Info("create node");

            var query = UriExtension.GetQueryComponents(req.Url);

            Guid poolId = Guid.Parse(query["poolId"]);
            var poolName = PoolName.Parse(query["poolName"]);
            Guid machineId = Guid.Parse(query["machineId"]);

            Guid? scaleSetId = default;
            if (query.ContainsKey("scaleSetId")) {
                scaleSetId = Guid.Parse(query["scaleSetId"]);
            }

            string version = query["version"];

            bool isNew = UriExtension.GetBool("isNew", query, false);

            var node = await _nodeOps.Create(poolId, poolName, machineId, scaleSetId, version, isNew);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(JsonSerializer.Serialize(node, EntityConverter.GetJsonSerializerOptions()));
            return resp;
        }

        [Function("GetDeadNodesTestHook")]
        public async Task<HttpResponseData> GetDeadNodes([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "testhooks/nodeOperations/getDeadNodes")] HttpRequestData req) {

            _log.Info("get dead nodes");

            var query = UriExtension.GetQueryComponents(req.Url);

            Guid scaleSetId = Guid.Parse(query["scaleSetId"]);
            TimeSpan timeSpan = TimeSpan.Parse(query["timeSpan"]);

            var nodes = await (_nodeOps.GetDeadNodes(scaleSetId, timeSpan).ToListAsync());
            var json = JsonSerializer.Serialize(nodes, EntityConverter.GetJsonSerializerOptions());
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(json);
            return resp;
        }


        [Function("MarkTasksStoppedEarly")]
        public async Task<HttpResponseData> MarkTasksStoppedEarly([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "testhooks/nodeOperations/markTasksStoppedEarly")] HttpRequestData req) {
            _log.Info("mark tasks stopped early");

            var s = await req.ReadAsStringAsync();
            var markTasks = JsonSerializer.Deserialize<MarkTasks>(s!, EntityConverter.GetJsonSerializerOptions());
            await _nodeOps.MarkTasksStoppedEarly(markTasks!.node, markTasks.error);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            return resp;
        }
    }
}
#endif
