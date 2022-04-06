using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service.OneFuzzLib
{
    internal class Events
    {
        public void SendEvent(BaseEvent anEvent) {
            var eventType = anEvent.GetEventType();

            var event_message = new EventMessage(
                Guid.NewGuid(),
                eventType,
                anEvent,
                Guid.NewGuid(), // todo
                "test" //todo
            );
            
        }
    }
}
