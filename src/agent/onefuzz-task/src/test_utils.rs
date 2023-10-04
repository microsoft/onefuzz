pub mod arbitraries {
    use std::path::PathBuf;

    use onefuzz::{blob::BlobContainerUrl, machine_id::MachineIdentity, syncdir::SyncedDir};
    use onefuzz_telemetry::{InstanceTelemetryKey, MicrosoftTelemetryKey};
    use proptest::{option, prelude::*};
    use reqwest::Url;
    use uuid::Uuid;
    
    use crate::tasks::config::CommonConfig;
    
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
            url in r"https?://(www\.)?[-a-zA-Z0-9]{1,256}\.[a-zA-Z0-9]{1,6}([-a-zA-Z0-9]*)"
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
        fn arb_common_config(tag_limit: usize)(
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
            tags in prop::collection::hash_map(".*", ".*", tag_limit),
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
            arb_common_config(10).boxed()
        }
    }
    
    // Make a trait out of this and add it to a common test module
    impl CommonConfig {
        // Get all the fields from the type that are passed to the expander
    }
}
