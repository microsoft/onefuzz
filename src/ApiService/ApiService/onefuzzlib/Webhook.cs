using Microsoft.OneFuzz.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApiService.OneFuzzLib
{
    public interface IWebhook
    {
        
        
    }

    public class Webhook
    {
        private readonly IStorage _storage;

        public Webhook(IStorage storage)
        {
            _storage = storage;
        }

        public void SendEvent(EventMessage eventMessage)
        {

            //for webhook in get_webhooks_cached():
            //if event_message.event_type not in webhook.event_types:
            //    continue

            //webhook._add_event(event_message)
        }

        public IAsyncEnumerable<Webhook> GetWebhooksCached()
        {
            throw new NotImplementedException();
        }

    }
}
