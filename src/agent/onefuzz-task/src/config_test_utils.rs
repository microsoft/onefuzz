use onefuzz::expand::{GetExpand, PlaceHolder};

// Moving this trait method into the GetExpand trait--and returning `Vec<(PlaceHolder, Box<dyn Any>)>` instead
// would let us use define a default implementation for `get_expand()` while also coupling the expand values we
// test with those we give to the expander.
// It seems to me like a non-trivial (and perhaps bad) design change though.
pub trait GetExpandFields: GetExpand {
    fn get_expand_fields(&self) -> Vec<(PlaceHolder, String)>;
}

pub mod arbitraries {
    use std::path::PathBuf;

    use onefuzz::{blob::BlobContainerUrl, machine_id::MachineIdentity, syncdir::SyncedDir};
    use onefuzz_telemetry::{InstanceTelemetryKey, MicrosoftTelemetryKey};
    use proptest::{option, prelude::*};
    use reqwest::Url;
    use uuid::Uuid;
    
    use crate::tasks::{config::CommonConfig, analysis, merge};
    
    prop_compose! {
        fn arb_uuid()(
            uuid in "[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}"
        ) -> Uuid {
            Uuid::parse_str(&uuid).unwrap()
        }
    }
    
    prop_compose! {
        fn arb_instance_telemetry_key()(
            uuid in arb_uuid()
        ) -> InstanceTelemetryKey {
            InstanceTelemetryKey::new(uuid)
        }
    }
    
    prop_compose! {
        fn arb_microsoft_telemetry_key()(
            uuid in arb_uuid()
        ) -> MicrosoftTelemetryKey {
            MicrosoftTelemetryKey::new(uuid)
        }
    }
    
    prop_compose! {
        fn arb_url()(
            // Don't use this for any url that isn't just being used for a string comparison (as for the config tests)
            url in r"https?://(www\.)?[-a-zA-Z0-9]{1,256}\.[a-zA-Z]{1,6}([-a-zA-Z]*)"
        ) -> Url {
            match Url::parse(&url) {
                Ok(url) => url,
                Err(err) => panic!("invalid url generated ({}): {}", err, url),
            }
        }
    }
    
    prop_compose! {
        // Todo: consider a better way to generate a path
        fn arb_pathbuf()(
            path in "src"
        ) -> PathBuf {
            PathBuf::from(path)
        }
    }
    
    prop_compose! {
        fn arb_machine_identity()(
            machine_id in arb_uuid(),
            machine_name in ".*",
            scaleset_name in ".*",
        ) -> MachineIdentity {
            MachineIdentity {
                machine_id,
                machine_name,
                scaleset_name: Some(scaleset_name),
            }
        }
    }
    
    fn arb_blob_container_url() -> impl Strategy<Value = BlobContainerUrl> {
        prop_oneof![
            arb_url().prop_map(BlobContainerUrl::BlobContainer),
            arb_pathbuf().prop_map(BlobContainerUrl::Path),
        ]
    }
    
    prop_compose! {
        fn arb_synced_dir()(
            local_path in arb_pathbuf(),
            remote_path in option::of(arb_blob_container_url()),
        ) -> SyncedDir {
            SyncedDir {
                local_path,
                remote_path,
            }
        }
    }

    prop_compose! {
        fn arb_string_vec_no_vars()(
            // I don't know how to figure out the expected value of the target options if they could contain variables (e.g. {machine_id})
            // This should be fine since this isn't used to test nested expansion
            options in prop::collection::vec("[^{}]*", 10),
        ) -> Vec<String> {
            options
        }
    }
    

    prop_compose! {
        fn arb_common_config()(
            job_id in arb_uuid(),
            task_id in arb_uuid(),
            instance_id in arb_uuid(),
            heartbeat_queue in option::of(arb_url()),
            job_result_queue in option::of(arb_url()),
            instance_telemetry_key in option::of(arb_instance_telemetry_key()), // consider implementing Arbitrary for these types for a canonical way to generate them
            microsoft_telemetry_key in option::of(arb_microsoft_telemetry_key()), // We can probably derive Arbitrary if it's implemented for the composing types like Url
            logs in option::of(arb_url()),
            setup_dir in arb_pathbuf(),
            extra_setup_dir in option::of(arb_pathbuf()),
            extra_output in option::of(arb_synced_dir()),
            min_available_memory_mb in any::<u64>(),
            machine_identity in arb_machine_identity(),
            tags in prop::collection::hash_map(".*", ".*", 10),
            from_agent_to_task_endpoint in ".*",
            from_task_to_agent_endpoint in ".*",
        ) -> CommonConfig {
            CommonConfig {
                job_id,
                task_id,
                instance_id,
                heartbeat_queue,
                job_result_queue,
                instance_telemetry_key,
                microsoft_telemetry_key,
                logs,
                setup_dir,
                extra_setup_dir,
                extra_output,
                min_available_memory_mb,
                machine_identity,
                tags,
                from_agent_to_task_endpoint,
                from_task_to_agent_endpoint,
            }
        }
    }
    
    impl Arbitrary for CommonConfig {
        type Parameters = ();
        type Strategy = BoxedStrategy<Self>;
    
        fn arbitrary_with(_args: Self::Parameters) -> Self::Strategy {
            arb_common_config().boxed()
        }
    }

    prop_compose! {
        fn arb_analysis_config()(
            analyzer_exe in Just("src/lib.rs".to_string()),
            analyzer_options in arb_string_vec_no_vars(),
            analyzer_env in prop::collection::hash_map(".*", ".*", 10),
            target_exe in arb_pathbuf(),
            target_options in arb_string_vec_no_vars(),
            input_queue in Just(None),
            crashes in option::of(arb_synced_dir()),
            analysis in arb_synced_dir(),
            tools in option::of(arb_synced_dir()),
            reports in option::of(arb_synced_dir()),
            unique_reports in option::of(arb_synced_dir()),
            no_repro in option::of(arb_synced_dir()),
            common in arb_common_config(),
        ) -> analysis::generic::Config {
            analysis::generic::Config {
                analyzer_exe,
                analyzer_options,
                analyzer_env,
                target_exe,
                target_options,
                input_queue,
                crashes,
                analysis,
                tools,
                reports,
                unique_reports,
                no_repro,
                common,
            }
        }
    }

    impl Arbitrary for analysis::generic::Config {
        type Parameters = ();
        type Strategy = BoxedStrategy<Self>;

        fn arbitrary_with(_args: Self::Parameters) -> Self::Strategy {
            arb_analysis_config().boxed()
        }
    }

    prop_compose! {
        fn arb_merge_config()(
            supervisor_exe in Just("src/lib.rs".to_string()),
            supervisor_options in arb_string_vec_no_vars(),
            supervisor_env in prop::collection::hash_map(".*", ".*", 10),
            supervisor_input_marker in ".*",
            target_exe in arb_pathbuf(),
            target_options in arb_string_vec_no_vars(),
            target_options_merge in any::<bool>(),
            tools in arb_synced_dir(),
            input_queue in arb_url(),
            inputs in arb_synced_dir(),
            unique_inputs in arb_synced_dir(),
            common in arb_common_config(),
        ) -> merge::generic::Config {
            merge::generic::Config {
                supervisor_exe,
                supervisor_options,
                supervisor_env,
                supervisor_input_marker,
                target_exe,
                target_options,
                target_options_merge,
                tools,
                input_queue,
                inputs,
                unique_inputs,
                common,
            }
        }
    }

    impl Arbitrary for merge::generic::Config {
        type Parameters = ();
        type Strategy = BoxedStrategy<Self>;

        fn arbitrary_with(_args: Self::Parameters) -> Self::Strategy {
            arb_merge_config().boxed()
        }
    }
}
