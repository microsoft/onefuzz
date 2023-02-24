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

    public static async Async.Task<bool> IsValidScribanNotificationTemplate(IOnefuzzContext context, ILogTracer log, NotificationTemplate template) {
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
            log.Exception(e);
            return false;
        }
    }

    public static async Async.Task<TemplateValidationResponse> ValidateScribanTemplate(IOnefuzzContext context, ILogTracer log, TemplateRenderContext? renderContext, string template) {
        var instanceUrl = context.ServiceConfiguration.OneFuzzInstance!;

        var (renderer, templateRenderContext) = await GenerateTemplateRenderContext(context, log, renderContext);

        var renderedTemaplate = await renderer.Render(template, new Uri(instanceUrl), strictRendering: true);

        return new TemplateValidationResponse(
            renderedTemaplate,
            templateRenderContext
        );
    }

    private static async Async.Task<(NotificationsBase.Renderer, TemplateRenderContext)> GenerateTemplateRenderContext(IOnefuzzContext context, ILogTracer log, TemplateRenderContext? templateRenderContext) {
        if (templateRenderContext != null) {
            log.Info($"Using custom TemplateRenderContext");
        } else {
            log.Info($"Generating TemplateRenderContext");
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
        var reportContainer = templateRenderContext?.ReportContainer ?? Container.Parse("example-container-name");
        var reportFileName = templateRenderContext?.ReportFilename ?? "example file name";
        var reproCmd = templateRenderContext?.ReproCmd ?? "onefuzz command to create a repro";
        var report = templateRenderContext?.Report ?? new Report(
                inputUrl.ToString(),
                null,
                executable,
                crashType,
                crashSite,
                callStack,
                callStackSha,
                inputSha,
                null,
                taskId,
                jobId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );

        var task = new Task(
                jobId,
                taskId,
                taskState,
                os,
                templateRenderContext?.Task ?? new TaskConfig(
                    jobId,
                    null,
                    new TaskDetails(
                        taskType,
                        duration
                    )
                )
            );

        var job = new Job(
                jobId,
                jobState,
                templateRenderContext?.Job ?? new JobConfig(
                    project,
                    jobName,
                    buildName,
                    duration,
                    null
                )
            );

        var renderer = await NotificationsBase.Renderer.ConstructRenderer(
            context,
            reportContainer,
            reportFileName,
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
            reproCmd
        );

        return (renderer, templateRenderContext);
    }

    public async static Async.Task<(bool didModify, AdoTemplate template)> ConvertToScriban(AdoTemplate template, bool attemptRender = false, IOnefuzzContext? context = null, ILogTracer? log = null) {
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

    public async static Async.Task<(bool didModify, GithubIssuesTemplate template)> ConvertToScriban(GithubIssuesTemplate template, bool attemptRender = false, IOnefuzzContext? context = null, ILogTracer? log = null) {
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
