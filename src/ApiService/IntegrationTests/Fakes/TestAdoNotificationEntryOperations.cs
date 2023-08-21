using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service;
namespace IntegrationTests.Fakes;

public sealed class TestAdoNotificationEntryOperations : AdoNotificationEntryOperations {
    public TestAdoNotificationEntryOperations(ILogger<AdoNotificationEntryOperations> log, IOnefuzzContext context)
        : base(log, context) { }
}
