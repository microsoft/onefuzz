using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class SchedulerTests {

    IEnumerable<Task> BuildTasks(int size) {
        return Enumerable.Range(0, size).Select(i =>
            new Task(
                Guid.Empty,
                Guid.NewGuid(),
                TaskState.Init,
                Os.Linux,
                new TaskConfig(
                    Guid.Empty,
                    null,
                    new TaskDetails(
                        Type: TaskType.LibfuzzerFuzz,
                        Duration: 1,
                        TargetExe: "fuzz.exe",
                        TargetEnv: new Dictionary<string, string>(),
                        TargetOptions: new List<string>()),
                    Pool: new TaskPool(1, "pool"),
                    Containers: new List<TaskContainers> { new TaskContainers(ContainerType.Setup, new Container("setup")) },
                    Colocate: true

                ),
                null,
                null,
                null,
                null,
                null)

            );
    }

    [Fact]
    public void TestAllColocate() {
        // all tasks should land in one bucket

        var tasks = BuildTasks(10).Select(task => task with { Config = task.Config with { Colocate = true } }
        ).ToList();

        var buckets = Scheduler.BucketTasks(tasks);
        foreach (var bucket in buckets) {
            Assert.True(10 >= bucket.Count());
        }
        CheckBuckets(buckets, tasks, 1);
    }

    [Fact]
    public void TestPartialColocate() {
        // 2 tasks should land on their own, the rest should be colocated into a
        // single bucket.

        var tasks = BuildTasks(10).Select((task, i) => {
            return i switch {
                0 => task with { Config = task.Config with { Colocate = null } },
                1 => task with { Config = task.Config with { Colocate = false } },
                _ => task
            };
        }).ToList();
        var buckets = Scheduler.BucketTasks(tasks);
        var lengths = buckets.Select(b => b.Count()).OrderBy(x => x);
        Assert.Equal(new[] { 1, 1, 8 }, lengths);
        CheckBuckets(buckets, tasks, 3);
    }

    [Fact]
    public void TestAlluniqueJob() {
        // everything has a unique job_id
        var tasks = BuildTasks(10).Select(task => {
            var jobId = Guid.NewGuid();
            return task with { JobId = jobId, Config = task.Config with { JobId = jobId } };
        }).ToList();

        var buckets = Scheduler.BucketTasks(tasks);
        foreach (var bucket in buckets) {
            Assert.True(1 >= bucket.Count());
        }
        CheckBuckets(buckets, tasks, 10);
    }

    [Fact]
    public void TestMultipleJobBuckets() {
        // at most 3 tasks per bucket, by job_id
        var tasks = BuildTasks(10).Chunk(3).SelectMany(taskChunk => {
            var jobId = Guid.NewGuid();
            return taskChunk.Select(task => task with { JobId = jobId, Config = task.Config with { JobId = jobId } });
        }).ToList();

        var buckets = Scheduler.BucketTasks(tasks);
        foreach (var bucket in buckets) {
            Assert.True(3 >= bucket.Count());
        }
        CheckBuckets(buckets, tasks, 4);
    }

    [Fact]
    public void TestManyBuckets() {
        var jobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tasks = BuildTasks(100).Select((task, i) => {
            var containers = new List<TaskContainers>(task.Config.Containers!);
            if (i % 4 == 0) {
                containers[0] = containers[0] with { Name = new Container("setup2") };
            }
            return task with {
                JobId = i % 2 == 0 ? jobId : task.JobId,
                Os = i % 3 == 0 ? Os.Windows : task.Os,
                Config = task.Config with {
                    JobId = i % 2 == 0 ? jobId : task.Config.JobId,
                    Containers = containers,
                    Pool = i % 5 == 0 ? task.Config.Pool! with { PoolName = "alternate-pool" } : task.Config.Pool
                }
            };
        }).ToList();

        var buckets = Scheduler.BucketTasks(tasks);

        CheckBuckets(buckets, tasks, 12);
    }

    void CheckBuckets(ILookup<Scheduler.BucketId, Task> buckets, List<Task> tasks, int bucketCount) {
        Assert.Equal(buckets.Count, bucketCount);

        foreach (var task in tasks) {
            var seen = false;
            foreach (var bucket in buckets) {
                if (bucket.Contains(task)) {
                    Assert.False(seen);
                    seen = true;
                }
            }
            Assert.True(seen);
        }

    }

}
