// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use onefuzz::libfuzzer::LibFuzzer;

use crate::tasks::fuzz::libfuzzer::common;
use crate::tasks::utils::{extra_setup, try_resolve_setup_relative_path};

/// Generic LibFuzzer with no special extra configuration.
///
/// Its configuration is fully controlled by the user, up to the constraints of the
/// `LibFuzzer` wrapper itself.
#[derive(Debug)]
pub struct GenericLibFuzzer;

#[async_trait]
impl common::LibFuzzerType for GenericLibFuzzer {
    type Config = ();

    async fn from_config(config: &common::Config<Self>) -> Result<LibFuzzer> {
        let target_exe =
            try_resolve_setup_relative_path(&config.common.setup_dir, &config.target_exe).await?;

        Ok(LibFuzzer::new(
            target_exe,
            config.target_options.clone(),
            config.target_env.clone(),
            config.common.setup_dir.clone(),
            config.common.extra_setup_dir.clone(),
            config
                .common
                .extra_output
                .as_ref()
                .map(|x| x.local_path.clone()),
            config.common.machine_identity.clone(),
        ))
    }

    async fn extra_setup(config: &common::Config<Self>) -> Result<()> {
        extra_setup(&config.common.setup_dir, &config.target_exe).await
    }
}

pub type Config = common::Config<GenericLibFuzzer>;
pub type LibFuzzerFuzzTask = common::LibFuzzerFuzzTask<GenericLibFuzzer>;
