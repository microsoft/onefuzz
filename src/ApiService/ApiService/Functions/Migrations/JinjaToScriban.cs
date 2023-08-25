using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class JinjaToScriban {

    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;


    public JinjaToScriban(ILogger<JinjaToScriban> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("JinjaToScriban")]
    [Authorize(Allow.Admin)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="migrations/jinja_to_scriban")]
        HttpRequestData req) {

        var request = await RequestHandling.ParseRequest<JinjaToScribanMigrationPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "JinjaToScriban");
        }

        _log.LogInformation("Finding notifications to migrate");

        var notifications = _context.NotificationOperations.SearchAll()
            .SelectAwait(async notification => {
                var (didModify, config) = notification.Config switch {
                    TeamsTemplate => (false, notification.Config),
                    AdoTemplate adoTemplate => await JinjaTemplateAdapter.ConvertToScriban(adoTemplate),
                    GithubIssuesTemplate githubIssuesTemplate => await JinjaTemplateAdapter.ConvertToScriban(githubIssuesTemplate),
                    _ => throw new NotImplementedException("Unexpected notification configuration type")
                };

                return (DidModify: didModify, Notification: notification with { Config = config });
            })
            .Where(notificationTuple => notificationTuple.DidModify)
            .Select(notificationTuple => notificationTuple.Notification);

        if (request.OkV.DryRun) {
            _log.LogInformation("Finding notifications to migrate");
            return await RequestHandling.Ok(req, new JinjaToScribanMigrationDryRunResponse(
                await notifications.Select(notification => notification.NotificationId).ToListAsync()
            ));
        }

        _log.LogInformation("Attempting to migrate {count} items", await notifications.CountAsync());

        var updatedNotificationsIds = new List<Guid>();
        var failedNotificationIds = new List<Guid>();

        await foreach (var notification in notifications) {
            try {
                var r = await _context.NotificationOperations.Replace(notification);
                if (r.IsOk) {
                    updatedNotificationsIds.Add(notification.NotificationId);
                    _log.LogInformation("Migrated notification: {id} to scriban", notification.NotificationId);
                } else {
                    failedNotificationIds.Add(notification.NotificationId);
                    _log.LogOneFuzzError(Error.Create(ErrorCode.UNABLE_TO_UPDATE, r.ErrorV.Reason, r.ErrorV.Status.ToString()));
                }
            } catch (Exception ex) {
                failedNotificationIds.Add(notification.NotificationId);
                _log.LogError(ex, "Failed to migrate notification to scriban {notificationId}", notification.NotificationId);
            }
        }

        return await RequestHandling.Ok(req, new JinjaToScribanMigrationResponse(updatedNotificationsIds, failedNotificationIds));
    }
}
