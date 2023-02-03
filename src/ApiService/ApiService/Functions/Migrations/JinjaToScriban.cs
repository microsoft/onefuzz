using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Microsoft.OneFuzz.Service.Functions;

public class JinjaToScriban {

    private readonly ILogTracer _log;
    private readonly IEndpointAuthorization _auth;
    private readonly IOnefuzzContext _context;


    public JinjaToScriban(ILogTracer log, IEndpointAuthorization auth, IOnefuzzContext context) {
        _log = log;
        _auth = auth;
        _context = context;
    }

    [Function("JinjaToScriban")]
    public Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route="migrations/jinja_to_scriban")]
        HttpRequestData req)
        => _auth.CallIfUser(req, Post);

    private async Async.Task<HttpResponseData> Post(HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<JinjaToScribanMigrationPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(
                req,
                request.ErrorV,
                "JinjaToScriban");
        }

        var answer = await _auth.CheckRequireAdmins(req);
        if (!answer.IsOk) {
            return await _context.RequestHandling.NotOk(req, answer.ErrorV, "JinjaToScriban");
        }

        _log.Info($"Finding notifications to migrate");

        var notifications = _context.NotificationOperations.SearchAll()
            .Select(notification => {
                var (didModify, config) = notification.Config switch {
                    TeamsTemplate => (false, notification.Config),
                    AdoTemplate adoTemplate => ConvertToScriban(adoTemplate),
                    GithubIssuesTemplate githubIssuesTemplate => ConvertToScriban(githubIssuesTemplate),
                    _ => throw new NotImplementedException("Unexpected notification configuration type")
                };

                return (DidModify: didModify, Notification: notification with { Config = config });
            })
            .Where(notificationTuple => notificationTuple.DidModify)
            .Select(notificationTuple => notificationTuple.Notification);

        if (request.OkV.DryRun) {
            _log.Info($"Dry run scriban migration");
            return await RequestHandling.Ok(req, new JinjaToScribanMigrationDryRunResponse(
                await notifications.Select(notification => notification.NotificationId).ToListAsync()
            ));
        }

        _log.Info($"Attempting to migrate {await notifications.CountAsync()} items");

        var updatedNotificationsIds = new List<Guid>();
        var failedNotificationIds = new List<Guid>();

        await foreach (var notification in notifications) {
            try {
                var r = await _context.NotificationOperations.Replace(notification);
                if (r.IsOk) {
                    updatedNotificationsIds.Add(notification.NotificationId);
                    _log.Info($"Migrated notification: {notification.NotificationId} to jinja");
                } else {
                    failedNotificationIds.Add(notification.NotificationId);
                    _log.Error(new Error(ErrorCode.UNABLE_TO_UPDATE, new[] { r.ErrorV.Reason, r.ErrorV.Status.ToString() }));
                }
            } catch (Exception ex) {
                failedNotificationIds.Add(notification.NotificationId);
                _log.Exception(ex);
            }
        }

        return await RequestHandling.Ok(req, new JinjaToScribanMigrationResponse(updatedNotificationsIds, failedNotificationIds));
    }

    private static (bool didModify, AdoTemplate template) ConvertToScriban(AdoTemplate template) {
        var didModify = false;

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Project)) {
            didModify = true;
            template = template with {
                Project = JinjaTemplateAdapter.AdaptForScriban(template.Project)
            };
        }

        foreach (var item in template.AdoFields) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(item.Value)) {
                template.AdoFields[item.Key] = JinjaTemplateAdapter.AdaptForScriban(item.Value);
                didModify = true;
            }
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Type)) {
            didModify = true;
            template = template with {
                Type = JinjaTemplateAdapter.AdaptForScriban(template.Type)
            };
        }

        if (template.Comment != null && JinjaTemplateAdapter.IsJinjaTemplate(template.Comment)) {
            didModify = true;
            template = template with {
                Comment = JinjaTemplateAdapter.AdaptForScriban(template.Comment)
            };
        }

        foreach (var item in template.OnDuplicate.AdoFields) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(item.Value)) {
                template.OnDuplicate.AdoFields[item.Key] = JinjaTemplateAdapter.AdaptForScriban(item.Value);
                didModify = true;
            }
        }

        if (template.OnDuplicate.Comment != null && JinjaTemplateAdapter.IsJinjaTemplate(template.OnDuplicate.Comment)) {
            didModify = true;
            template = template with {
                OnDuplicate = template.OnDuplicate with {
                    Comment = JinjaTemplateAdapter.AdaptForScriban(template.OnDuplicate.Comment)
                }
            };
        }

        return (didModify, template);
    }

    private static (bool didModify, GithubIssuesTemplate template) ConvertToScriban(GithubIssuesTemplate template) {
        var didModify = false;

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.UniqueSearch.str)) {
            didModify = true;
            template = template with {
                UniqueSearch = template.UniqueSearch with {
                    str = JinjaTemplateAdapter.AdaptForScriban(template.UniqueSearch.str)
                }
            };
        }

        if (!string.IsNullOrEmpty(template.UniqueSearch.Author) && JinjaTemplateAdapter.IsJinjaTemplate(template.UniqueSearch.Author)) {
            didModify = true;
            template = template with {
                UniqueSearch = template.UniqueSearch with {
                    Author = JinjaTemplateAdapter.AdaptForScriban(template.UniqueSearch.Author)
                }
            };
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Title)) {
            didModify = true;
            template = template with {
                Title = JinjaTemplateAdapter.AdaptForScriban(template.Title)
            };
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Body)) {
            didModify = true;
            template = template with {
                Body = JinjaTemplateAdapter.AdaptForScriban(template.Body)
            };
        }

        if (!string.IsNullOrEmpty(template.OnDuplicate.Comment) && JinjaTemplateAdapter.IsJinjaTemplate(template.OnDuplicate.Comment)) {
            didModify = true;
            template = template with {
                OnDuplicate = template.OnDuplicate with {
                    Comment = JinjaTemplateAdapter.AdaptForScriban(template.OnDuplicate.Comment)
                }
            };
        }

        if (template.OnDuplicate.Labels.Any()) {
            template = template with {
                OnDuplicate = template.OnDuplicate with {
                    Labels = template.OnDuplicate.Labels.Select(label => {
                        if (JinjaTemplateAdapter.IsJinjaTemplate(label)) {
                            didModify = true;
                            return JinjaTemplateAdapter.AdaptForScriban(label);
                        }
                        return label;
                    }).ToList()
                }
            };
        }

        if (template.Assignees.Any()) {
            template = template with {
                Assignees = template.Assignees.Select(assignee => {
                    if (JinjaTemplateAdapter.IsJinjaTemplate(assignee)) {
                        didModify = true;
                        return JinjaTemplateAdapter.AdaptForScriban(assignee);
                    }
                    return assignee;
                }).ToList()
            };
        }

        if (template.Labels.Any()) {
            template = template with {
                Labels = template.Labels.Select(label => {
                    if (JinjaTemplateAdapter.IsJinjaTemplate(label)) {
                        didModify = true;
                        return JinjaTemplateAdapter.AdaptForScriban(label);
                    }
                    return label;
                }).ToList()
            };
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Organization)) {
            didModify = true;
            template = template with {
                Organization = JinjaTemplateAdapter.AdaptForScriban(template.Organization)
            };
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Repository)) {
            didModify = true;
            template = template with {
                Repository = JinjaTemplateAdapter.AdaptForScriban(template.Repository)
            };
        }

        return (didModify, template);
    }
}
