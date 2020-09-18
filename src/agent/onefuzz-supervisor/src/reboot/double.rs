// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use super::*;

#[derive(Clone, Debug, Default)]
pub struct RebootDouble {
    pub saved: Vec<RebootContext>,
    pub invoked: bool,
}

#[async_trait]
impl IReboot for RebootDouble {
    async fn save_context(&mut self, ctx: RebootContext) -> Result<()> {
        self.saved.push(ctx);
        Ok(())
    }

    async fn load_context(&mut self) -> Result<Option<RebootContext>> {
        Ok(self.saved.pop())
    }

    fn invoke(&mut self) -> Result<()> {
        self.invoked = true;
        Ok(())
    }
}
