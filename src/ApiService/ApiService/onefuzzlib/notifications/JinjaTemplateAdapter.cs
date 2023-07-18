using Microsoft.Extensions.Logging;
namespace Microsoft.OneFuzz.Service;

public class JinjaTemplateAdapter {
    public static bool IsJinjaTemplate(string jinjaTemplate) {
        return jinjaTemplate.Contains("{% endfor %}")
            || jinjaTemplate.Contains("{% endif %}");
    }
    public static string AdaptForScriban(string jinjaTemplate) {
        return jinjaTemplate.Replace("endfor", "end")
            .Replace("endif", "end")
            .Replace("{%", "{{")
            .Replace("%}", "}}");
    }

    public static async Async.Task<bool> IsValidScribanNotificationTemplate(IOnefuzzContext context, ILogger log, NotificationTemplate template) {
        try {
            var (didModify, _) = template switch {
                TeamsTemplate => (false, template),
                AdoTemplate adoTemplate => await ConvertToScriban(adoTemplate, attemptRender: true, context, log),
                GithubIssuesTemplate githubTemplate => await ConvertToScriban(githubTemplate, attemptRender: true, context, log),
                _ => throw new ArgumentOutOfRangeException(nameof(template), "Unexpected notification template type")
            };

            if (!didModify) {
                return true;
            }
            return false;
        } catch (Exception e) {
            log.LogError(e, "IsValidScribanNotificationTemplate");
            return false;
        }
    }

    public static async Async.Task<TemplateValidationResponse> ValidateScribanTemplate(IOnefuzzContext context, ILogger log, TemplateRenderContext? renderContext, string template) {
        var instanceUrl = context.ServiceConfiguration.OneFuzzInstance!;

        var (renderer, templateRenderContext) = await GenerateTemplateRenderContext(context, log, renderContext);

        var renderedTemaplate = renderer.Render(template, new Uri(instanceUrl), strictRendering: true);

        return new TemplateValidationResponse(
            renderedTemaplate,
            templateRenderContext
        );
    }

    private static async Async.Task<(NotificationsBase.Renderer, TemplateRenderContext)> GenerateTemplateRenderContext(IOnefuzzContext context, ILogger log, TemplateRenderContext? templateRenderContext) {
        if (templateRenderContext != null) {
            log.LogInformation("Using custom TemplateRenderContext");
        } else {
            log.LogInformation("Generating TemplateRenderContext");
        }

        var targetUrl = templateRenderContext?.TargetUrl ?? new Uri("https://example.com/targetUrl");
        var inputUrl = templateRenderContext?.InputUrl ?? new Uri("https://example.com/inputUrl");
        var reportUrl = templateRenderContext?.ReportUrl ?? new Uri("https://example.com/reportUrl");
        var executable = "target.exe";
        var crashType = "some crash type";
        var crashSite = "some crash site";
        var callStack = new List<string>()
        {
            "stack frame 0",
            "stack frame 1"
        };
        var callStackSha = "call stack sha";
        var inputSha = "input sha";
        var taskId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var taskState = TaskState.Running;
        var jobState = JobState.Enabled;
        var os = Os.Linux;
        var taskType = TaskType.LibfuzzerFuzz;
        var duration = 100;
        var project = "some project";
        var jobName = "job name";
        var buildName = "build name";
        var account = "some account";
        var container = Container.Parse("container");
        var asanLog = "asan log";
        var scarinessScore = 5;
        var scarinessDescription = "super scary";
        var minimizedStack = new List<string> { "minimized stack frame 0", "minimized stack frame 1" };
        var minimizedStackSha = "abc123";
        var minimizedStackFunctionNames = new List<string> { "minimized stack function 0", "minimized stack function 1" };
        var minimizedStackFunctionNamesSha = "abc123";
        var minimizedStackFunctionLines = new List<string> { "minimized stack function line 0", "minimized stack function line 1" };
        var minimizedStackFunctionLinesSha = "abc123";
        var reportContainer = templateRenderContext?.ReportContainer ?? Container.Parse("example-container-name");
        var reportFileName = templateRenderContext?.ReportFilename ?? "example file name";
        var issueTitle = templateRenderContext?.IssueTitle ?? "example title";
        var reproCmd = templateRenderContext?.ReproCmd ?? "onefuzz command to create a repro";
        var toolName = "tool name";
        var toolVersion = "tool version";
        var onefuzzVersion = "onefuzz version";
        var report = templateRenderContext?.Report ?? new Report(
                inputUrl.ToString(),
                new BlobRef(account, container, reportFileName),
                executable,
                crashType,
                crashSite,
                callStack,
                callStackSha,
                inputSha,
                asanLog,
                taskId,
                jobId,
                scarinessScore,
                scarinessDescription,
                minimizedStack,
                minimizedStackSha,
                minimizedStackFunctionNames,
                minimizedStackFunctionNamesSha,
                minimizedStackFunctionLines,
                minimizedStackFunctionLinesSha,
                toolName,
                toolVersion,
                onefuzzVersion,
                reportUrl
            );

        var preReqTasks = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var targetExe = "target exe";
        var targetEnv = new Dictionary<string, string> { { "key", "value" } };
        var targetOptions = new List<string> { "option 1", "option 2" };
        var supervisorExe = "supervisor exe";
        var task = new Task(
                jobId,
                taskId,
                taskState,
                os,
                templateRenderContext?.Task ?? new TaskConfig(
                    jobId,
                    preReqTasks,
                    new TaskDetails(
                        taskType,
                        duration,
                        targetExe,
                        targetEnv,
                        targetOptions,
                        1,
                        true,
                        true,
                        true,
                        1,
                        true,
                        true,
                        true,
                        supervisorExe,
                        targetEnv,
                        targetOptions,
                        "supervisor input market",
                        "generator exe",
                        targetEnv,
                        targetOptions,
                        "analyzer exe",
                        targetEnv,
                        targetOptions,
                        ContainerType.Analysis,
                        "stats file",
                        StatsFormat.AFL,
                        true,
                        1,
                        1,
                        true,
                        targetOptions,
                        1,
                        new Dictionary<string, string>(),
                        "coverage filter",
                        "module allow list",
                        "source allow list",
                        "target assembly",
                        "target class",
                        "target method"
                    ),
                    new TaskVm(
                        Region.Parse("westus3"),
                        "some sku",
                        DefaultImages.Linux,
                        true,
                        1,
                        true
                    ),
                    new TaskPool(
                        1,
                        PoolName.Parse("poolname")
                    ),
                    new List<TaskContainers> {
                        new TaskContainers(ContainerType.Inputs, Container.Parse("inputs")),
                    },
                    targetEnv,
                    new List<TaskDebugFlag> { TaskDebugFlag.KeepNodeOnCompletion },
                    true
                ),
                Error.Create(ErrorCode.UNABLE_TO_FIND, "some error message"),
                new SecretValue<Authentication>(new Authentication("password", "public key", "private key")),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new(Guid.NewGuid(), Guid.NewGuid())
            );

        var job = new Job(
                jobId,
                jobState,
                templateRenderContext?.Job ?? new JobConfig(
                    project,
                    jobName,
                    buildName,
                    duration,
                    "logs"
                ),
                null,
                "some error",
                DateTimeOffset.UtcNow
            );

        var renderer = await NotificationsBase.Renderer.ConstructRenderer(
            context,
            reportContainer,
            reportFileName,
            issueTitle,
            report,
            log,
            task,
            job,
            targetUrl,
            inputUrl,
            reportUrl,
            scribanOnlyOverride: true
        );

        templateRenderContext ??= new TemplateRenderContext(
            report,
            task.Config,
            job.Config,
            reportUrl,
            inputUrl,
            targetUrl,
            reportContainer,
            reportFileName,
            issueTitle,
            reproCmd
        );

        return (renderer, templateRenderContext);
    }

    public static async Async.Task<(bool didModify, AdoTemplate template)> ConvertToScriban(AdoTemplate template, bool attemptRender = false, IOnefuzzContext? context = null, ILogger? log = null) {
        if (attemptRender) {
            context = context.EnsureNotNull("Required to render");
            log = log.EnsureNotNull("Required to render");
        }

        var didModify = false;

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Project)) {
            didModify = true;
            template = template with {
                Project = JinjaTemplateAdapter.AdaptForScriban(template.Project)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Project).IgnoreResult();
        }

        foreach (var item in template.AdoFields) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(item.Value)) {
                template.AdoFields[item.Key] = JinjaTemplateAdapter.AdaptForScriban(item.Value);
                didModify = true;
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, item.Value).IgnoreResult();
            }
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Type)) {
            didModify = true;
            template = template with {
                Type = JinjaTemplateAdapter.AdaptForScriban(template.Type)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Type).IgnoreResult();
        }

        if (template.Comment != null) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(template.Comment)) {
                didModify = true;
                template = template with {
                    Comment = JinjaTemplateAdapter.AdaptForScriban(template.Comment)
                };
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, template.Comment).IgnoreResult();
            }
        }

        foreach (var item in template.OnDuplicate.AdoFields) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(item.Value)) {
                template.OnDuplicate.AdoFields[item.Key] = JinjaTemplateAdapter.AdaptForScriban(item.Value);
                didModify = true;
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, item.Value).IgnoreResult();
            }
        }

        if (template.OnDuplicate.Comment != null) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(template.OnDuplicate.Comment)) {
                didModify = true;
                template = template with {
                    OnDuplicate = template.OnDuplicate with {
                        Comment = JinjaTemplateAdapter.AdaptForScriban(template.OnDuplicate.Comment)
                    }
                };
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, template.OnDuplicate.Comment).IgnoreResult();
            }
        }

        return (didModify, template);
    }

    public static async Async.Task<(bool didModify, GithubIssuesTemplate template)> ConvertToScriban(GithubIssuesTemplate template, bool attemptRender = false, IOnefuzzContext? context = null, ILogger? log = null) {
        if (attemptRender) {
            context = context.EnsureNotNull("Required to render");
            log = log.EnsureNotNull("Required to render");
        }

        var didModify = false;

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.UniqueSearch.str)) {
            didModify = true;
            template = template with {
                UniqueSearch = template.UniqueSearch with {
                    str = JinjaTemplateAdapter.AdaptForScriban(template.UniqueSearch.str)
                }
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.UniqueSearch.str).IgnoreResult();
        }


        if (!string.IsNullOrEmpty(template.UniqueSearch.Author)) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(template.UniqueSearch.Author)) {
                didModify = true;
                template = template with {
                    UniqueSearch = template.UniqueSearch with {
                        Author = JinjaTemplateAdapter.AdaptForScriban(template.UniqueSearch.Author)
                    }
                };
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, template.UniqueSearch.Author).IgnoreResult();
            }
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Title)) {
            didModify = true;
            template = template with {
                Title = JinjaTemplateAdapter.AdaptForScriban(template.Title)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Title).IgnoreResult();
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Body)) {
            didModify = true;
            template = template with {
                Body = JinjaTemplateAdapter.AdaptForScriban(template.Body)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Body).IgnoreResult();
        }

        if (!string.IsNullOrEmpty(template.OnDuplicate.Comment)) {
            if (JinjaTemplateAdapter.IsJinjaTemplate(template.OnDuplicate.Comment)) {
                didModify = true;
                template = template with {
                    OnDuplicate = template.OnDuplicate with {
                        Comment = JinjaTemplateAdapter.AdaptForScriban(template.OnDuplicate.Comment)
                    }
                };
            } else if (attemptRender) {
                await ValidateScribanTemplate(context!, log!, null, template.OnDuplicate.Comment).IgnoreResult();
            }
        }

        if (template.OnDuplicate.Labels.Any()) {
            template = template with {
                OnDuplicate = template.OnDuplicate with {
                    Labels = template.OnDuplicate.Labels.ToAsyncEnumerable().SelectAwait(async label => {
                        if (JinjaTemplateAdapter.IsJinjaTemplate(label)) {
                            didModify = true;
                            return JinjaTemplateAdapter.AdaptForScriban(label);
                        } else if (attemptRender) {
                            await ValidateScribanTemplate(context!, log!, null, label).IgnoreResult();
                        }
                        return label;
                    }).ToEnumerable().ToList()
                }
            };
        }

        if (template.Assignees.Any()) {
            template = template with {
                Assignees = template.Assignees.ToAsyncEnumerable().SelectAwait(async assignee => {
                    if (JinjaTemplateAdapter.IsJinjaTemplate(assignee)) {
                        didModify = true;
                        return JinjaTemplateAdapter.AdaptForScriban(assignee);
                    } else if (attemptRender) {
                        await ValidateScribanTemplate(context!, log!, null, assignee).IgnoreResult();
                    }
                    return assignee;
                }).ToEnumerable().ToList()
            };
        }

        if (template.Labels.Any()) {
            template = template with {
                Labels = template.Labels.ToAsyncEnumerable().SelectAwait(async label => {
                    if (JinjaTemplateAdapter.IsJinjaTemplate(label)) {
                        didModify = true;
                        return JinjaTemplateAdapter.AdaptForScriban(label);
                    } else if (attemptRender) {
                        await ValidateScribanTemplate(context!, log!, null, label).IgnoreResult();
                    }
                    return label;
                }).ToEnumerable().ToList()
            };
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Organization)) {
            didModify = true;
            template = template with {
                Organization = JinjaTemplateAdapter.AdaptForScriban(template.Organization)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Organization).IgnoreResult();
        }

        if (JinjaTemplateAdapter.IsJinjaTemplate(template.Repository)) {
            didModify = true;
            template = template with {
                Repository = JinjaTemplateAdapter.AdaptForScriban(template.Repository)
            };
        } else if (attemptRender) {
            await ValidateScribanTemplate(context!, log!, null, template.Repository).IgnoreResult();
        }

        return (didModify, template);
    }
}
