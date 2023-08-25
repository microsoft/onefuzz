namespace Microsoft.OneFuzz.Service;

public static class Defs {
    private static readonly ContainerDefinition _extraSetupContainer =
        new(
            Type: ContainerType.ExtraSetup,
            Compare: Compare.AtMost,
            Value: 1,
            Permissions: ContainerPermission.Read | ContainerPermission.List
        );

    private static readonly ContainerDefinition _extraOutputContainer =
        new(
            Type: ContainerType.ExtraOutput,
            Compare: Compare.AtMost,
            Value: 1,
            Permissions: ContainerPermission.Read
                | ContainerPermission.List
                | ContainerPermission.Write
        );

    private static readonly ContainerDefinition _setupContainer =
        new(
            Type: ContainerType.Setup,
            Compare: Compare.Equal,
            Value: 1,
            Permissions: ContainerPermission.Read | ContainerPermission.List
        );

    public static readonly IReadOnlyDictionary<TaskType, TaskDefinition> TASK_DEFINITIONS =
        new Dictionary<TaskType, TaskDefinition>()
        {
            {
                TaskType.Coverage,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.TargetMustUseInput,
                        // Deprecated. Retained for processing old table data.
                        TaskFeature.CoverageFilter,
                        TaskFeature.ModuleAllowlist,
                        TaskFeature.SourceAllowlist,
                    },
                    Vm: new VmDefinition(Compare: Compare.Equal, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Coverage,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.List
                                | ContainerPermission.Read
                                | ContainerPermission.Write
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.ReadonlyInputs
                )
            },
            {
                TaskType.DotnetCoverage,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CoverageFilter,
                        TaskFeature.TargetMustUseInput,
                    },
                    Vm: new VmDefinition(Compare: Compare.Equal, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Coverage,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.List
                                | ContainerPermission.Read
                                | ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.ReadonlyInputs
                )
            },
            {
                TaskType.DotnetCrashReport,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.MinimizedStackDepth,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.Crashes
                )
            },
            {
                TaskType.LibfuzzerDotnetFuzz,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetWorkers,
                        TaskFeature.EnsembleSyncDelay,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.ExpectCrashOnFailure,
                        TaskFeature.TargetAssembly,
                        TaskFeature.TargetClass,
                        TaskFeature.TargetMethod,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type:ContainerType.Crashdumps,
                            Compare: Compare.AtMost,
                            Value:1,
                            Permissions: ContainerPermission.Write
                            ),
                        new ContainerDefinition(
                            Type: ContainerType.Inputs,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 0,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.GenericAnalysis,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetOptions,
                        TaskFeature.AnalyzerExe,
                        TaskFeature.AnalyzerEnv,
                        TaskFeature.AnalyzerOptions,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Analysis,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.Crashes
                )
            },
            {
                TaskType.LibfuzzerFuzz,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetWorkers,
                        TaskFeature.EnsembleSyncDelay,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.ExpectCrashOnFailure,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type:ContainerType.Crashdumps,
                            Compare: Compare.AtMost,
                            Value:1,
                            Permissions: ContainerPermission.Write
                            ),
                        new ContainerDefinition(
                            Type: ContainerType.Inputs,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 0,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.LibfuzzerCrashReport,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.MinimizedStackDepth,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.Crashes
                )
            },
            {
                TaskType.LibfuzzerCoverage,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.CheckFuzzerHelp,
                    },
                    Vm: new VmDefinition(Compare: Compare.Equal, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Coverage,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.List
                                | ContainerPermission.Read
                                | ContainerPermission.Write
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.ReadonlyInputs
                )
            },
            {
                TaskType.LibfuzzerMerge,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.PreserveExistingOutputs,
                    },
                    Vm: new VmDefinition(Compare: Compare.Equal, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.UniqueInputs,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.List
                                | ContainerPermission.Read
                                | ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Inputs,
                            Compare: Compare.AtLeast,
                            Value: 0,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.GenericSupervisor,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetOptions,
                        TaskFeature.SupervisorExe,
                        TaskFeature.SupervisorEnv,
                        TaskFeature.SupervisorOptions,
                        TaskFeature.SupervisorInputMarker,
                        TaskFeature.WaitForFiles,
                        TaskFeature.StatsFile,
                        TaskFeature.EnsembleSyncDelay,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Inputs,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Coverage,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.GenericMerge,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetOptions,
                        TaskFeature.SupervisorExe,
                        TaskFeature.SupervisorEnv,
                        TaskFeature.SupervisorOptions,
                        TaskFeature.SupervisorInputMarker,
                        TaskFeature.StatsFile,
                        TaskFeature.PreserveExistingOutputs,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Inputs,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.GenericGenerator,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.GeneratorExe,
                        TaskFeature.GeneratorEnv,
                        TaskFeature.GeneratorOptions,
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.RenameOutput,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckAsanLog,
                        TaskFeature.CheckDebugger,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.EnsembleSyncDelay,
                        TaskFeature.TargetMustUseInput,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Tools,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtLeast,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.GenericCrashReport,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckAsanLog,
                        TaskFeature.CheckDebugger,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.MinimizedStackDepth,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    },
                    MonitorQueue: ContainerType.Crashes
                )
            },
            {
                TaskType.GenericRegression,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckAsanLog,
                        TaskFeature.CheckDebugger,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.ReportList,
                        TaskFeature.MinimizedStackDepth,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.RegressionReports,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
            {
                TaskType.LibfuzzerRegression,
                new TaskDefinition(
                    Features: new[]
                    {
                        TaskFeature.TargetExe,
                        TaskFeature.TargetEnv,
                        TaskFeature.TargetOptions,
                        TaskFeature.TargetTimeout,
                        TaskFeature.CheckFuzzerHelp,
                        TaskFeature.CheckRetryCount,
                        TaskFeature.ReportList,
                        TaskFeature.MinimizedStackDepth,
                    },
                    Vm: new VmDefinition(Compare: Compare.AtLeast, Value: 1),
                    Containers: new[]
                    {
                        _setupContainer,
                        new ContainerDefinition(
                            Type: ContainerType.RegressionReports,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Write
                                | ContainerPermission.Read
                                | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Crashes,
                            Compare: Compare.Equal,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.UniqueReports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.Reports,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.NoRepro,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        new ContainerDefinition(
                            Type: ContainerType.ReadonlyInputs,
                            Compare: Compare.AtMost,
                            Value: 1,
                            Permissions: ContainerPermission.Read | ContainerPermission.List
                        ),
                        _extraSetupContainer,
                        _extraOutputContainer,
                    }
                )
            },
        };
}
