﻿using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.OneFuzz.Service;
using Scriban;
using Xunit;

namespace Tests;


public class TemplateTests {
    // Original python template just works
    private static readonly string _defaultTemplate = "<a href='{{ input_url }}'>This input</a> caused the <a href='{{ target_url }}'>fuzz target</a> {{ report.executable }} to crash. The faulting input SHA256 hash is {{ report.input_sha256 }} <br>";

    // Original python template: "This is the call stack as determined by heuristics. You may wish to confirm this stack trace with a debugger via repro: <ul> {% for item in report.call_stack %} <li> {{ item }} </li> {% endfor %} </ul>"
    // Changes for dotnet:
    //     * Change "endfor" in python to "end"
    //     * Change "{% ... %}" in python to "{{ ... }}"
    private static readonly string _testString1 = "This is the call stack as determined by heuristics. You may wish to confirm this stack trace with a debugger via repro: <ul> {{ for item in report.call_stack }} <li> {{ item }} </li> {{ end }} </ul>";

    // Original python template just works
    private static readonly string _testString2 = "The OneFuzz job {{ task.job_id }} found a crash in <a href='{{ target_url }}'>{{ report.executable }}</a> with input <a href='{{ input_url }}'>{{ report.input_sha256 }}</a>. ASan log:<br><br>{{ report.asan_log }}";

    // Original python template: "The fuzzing target ({{ job.project }} {{ job.name }} {{ job.build }}) reported a crash. <br> {%if report.asan_log %} AddressSanitizer reported the following details: <br> <pre> {{ report.asan_log }} </pre> {% else %} Faulting call stack: <ul> {% for item in report.call_stack %} <li> {{ item }} </li> {% endfor %} </ul> <br> {% endif %} You can reproduce the issue remotely in OneFuzz by running the following command: <pre> {{ repro_cmd }} </pre>"
    // Changes for dotnet:
    //     * Change "endfor" in python to "end"
    //     * Change "endif" in python for "end"
    //     * Change "{% ... %}" in python to "{{ ... }}"
    //     * Change job.project -> job.config.project (same for job.name, job.build). This is actually a bug, it shouldn't work in python either
    private static readonly string _testString3 = "The fuzzing target ({{ job.config.project }} {{ job.config.name }} {{ job.config.build }}) reported a crash. <br> {{ if report.asan_log }} AddressSanitizer reported the following details: <br> <pre> {{ report.asan_log }} </pre> {{ else }} Faulting call stack: <ul> {{ for item in report.call_stack }} <li> {{ item }} </li> {{ end }} </ul> <br> {{ end }} You can reproduce the issue remotely in OneFuzz by running the following command: <pre> {{ repro_cmd }} </pre>";

    [Fact]
    public void CanFormatDefaultTemplate() {
        var template = Template.Parse(_defaultTemplate);
        template.Should().NotBeNull();

        var targetUrl = "https://targeturl.com";

        var report = GetReport();

        var output = template.Render(new {
            InputUrl = report.InputUrl!,
            TargetUrl = targetUrl,
            Report = report
        });

        output.Should().Contain(report.InputUrl);
        output.Should().Contain(targetUrl);
        output.Should().Contain(report.Executable);
        output.Should().Contain(report.InputSha256);
    }

    [Fact]
    public void CanFormatTemplateWithForLoop() {
        var template = Template.Parse(_testString1);
        template.Should().NotBeNull();

        var report = GetReport();

        var output = template.Render(new {
            Report = report
        });

        output.Should().ContainAll(report.CallStack);
    }

    [Fact]
    public void CanFormatWithMultipleComplexObjects() {
        var template = Template.Parse(_testString2);
        template.Should().NotBeNull();

        var targetUrl = "https://targeturl.com";

        var report = GetReport();
        var task = GetTask();

        var output = template.Render(new {
            InputUrl = report.InputUrl!,
            TargetUrl = targetUrl,
            Report = report,
            Task = task
        });

        output.Should().Contain(task.JobId.ToString());
        output.Should().Contain(targetUrl);
        output.Should().Contain(report.Executable);
        output.Should().Contain(report.InputUrl);
        output.Should().Contain(report.InputSha256);
        output.Should().Contain(report.AsanLog);
    }

    [Fact]
    public void CanFormatWithIfStatement() {
        var template = Template.Parse(_testString3);
        template.Should().NotBeNull();

        var reproCmd = "run the repro";

        var report = GetReport();
        var job = GetJob();

        var output = template.Render(new {
            ReproCmd = reproCmd,
            Report = report,
            Job = job
        });

        output.Should().Contain(job.Config.Project);
        output.Should().Contain(job.Config.Name);
        output.Should().Contain(job.Config.Build);
        output.Should().Contain(report.AsanLog);
        output.Should().Contain(reproCmd);

        // The template logic results in either the asan_log or call stack to be present
        output.Should().NotContainAny(report.CallStack);
    }

    private static Report GetReport() {
        return new Report(
            "https://example.com",
            null,
            "target.exe",
            string.Empty,
            string.Empty,
            new List<string>
            {
                "stack 1",
                "stack 2",
                "stack 3"
            },
            string.Empty,
            "deadbeef",
            "This is an asan log",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
    }

    private static Task GetTask() {
        var jobId = Guid.NewGuid();
        return new Task(
            jobId,
            Guid.NewGuid(),
            TaskState.Init,
            Os.Linux,
            new TaskConfig(
                jobId,
                null,
                new TaskDetails(
                    TaskType.LibfuzzerFuzz,
                    1
                )
            )
        );
    }

    private static Job GetJob() {
        return new Job(
            Guid.NewGuid(),
            JobState.Init,
            new JobConfig(
                "Test project",
                "Test name",
                "Test build",
                1,
                null
            )
        );
    }

}
