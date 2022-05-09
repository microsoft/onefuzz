using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

#if DEBUG
namespace ApiService.TestHooks {

    public class EventsTestHooks {
        private readonly ILogTracer _log;
        private readonly IConfigOperations _configOps;
        private readonly IEvents _events;

        public EventsTestHooks(ILogTracer log, IConfigOperations configOps, IEvents events) {
            _log = log.WithTag("TestHooks", nameof(EventsTestHooks));
            _configOps = configOps;
            _events = events;
        }

        [Function("LogEventTestHook")]
        public async Task<HttpResponseData> LogEvent([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "testhooks/events/logEvent")] HttpRequestData req) {
            _log.Info("Log event");

            var s = await req.ReadAsStringAsync();
            var msg = JsonSerializer.Deserialize<EventMessage>(s!, EntityConverter.GetJsonSerializerOptions());
            _events.LogEvent(msg!.Event);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            return resp;
        }


        [Function("SendEventTestHook")]
        public async Task<HttpResponseData> SendEvent([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "testhooks/events/sendEvent")] HttpRequestData req) {
            _log.Info("Send event");

            var s = await req.ReadAsStringAsync();
            var msg = JsonSerializer.Deserialize<EventMessage>(s!, EntityConverter.GetJsonSerializerOptions());
            await _events.SendEvent(msg!.Event);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            return resp;
        }
    }

}
#endif
