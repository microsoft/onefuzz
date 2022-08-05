// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use async_trait::async_trait;
use onefuzz::libfuzzer::LibFuzzer;

use crate::tasks::fuzz::libfuzzer::common;

/// Generic LibFuzzer with no special extra configuration.
///
/// Its configuration is fully controlled by the user, up to the constraints of the
/// `LibFuzzer` wrapper itself.
#[derive(Debug)]
pub struct GenericLibFuzzer;

#[async_trait]
impl common::LibFuzzerType for GenericLibFuzzer {
    type Config = ();

    fn from_config(config: &common::Config<Self>) -> LibFuzzer {
        LibFuzzer::new(
            &config.target_exe,
            config.target_options.clone(),
            config.target_env.clone(),
            &config.common.setup_dir,
        )
    }
}

pub type Config = common::Config<GenericLibFuzzer>;
pub type LibFuzzerFuzzTask = common::LibFuzzerFuzzTask<GenericLibFuzzer>;
