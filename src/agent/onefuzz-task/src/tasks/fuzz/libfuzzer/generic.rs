// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use onefuzz::libfuzzer::LibFuzzer;

use crate::tasks::fuzz::libfuzzer::common;

#[derive(Debug)]
pub struct GenericLibFuzzer;

impl common::LibFuzzerType for GenericLibFuzzer {
    type Config = ();

    fn from_config(config: &common::Config<Self>) -> LibFuzzer {
        LibFuzzer::new(
            &config.target_exe,
            &config.target_options,
            &config.target_env,
            &config.common.setup_dir,
        )
    }
}

pub type Config = common::Config<GenericLibFuzzer>;
pub type LibFuzzerFuzzTask = common::LibFuzzerFuzzTask<GenericLibFuzzer>;
