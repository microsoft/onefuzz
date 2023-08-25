// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::sync::{
    atomic::{self, AtomicBool},
    Arc,
};

use tokio::sync::RwLock;

use super::*;

#[derive(Debug, Default)]
pub struct RebootDouble {
    pub saved: Arc<RwLock<Vec<RebootContext>>>,
    pub invoked: AtomicBool,
}

impl Clone for RebootDouble {
    fn clone(&self) -> Self {
        Self {
            saved: self.saved.clone(),
            invoked: AtomicBool::new(self.invoked.load(atomic::Ordering::SeqCst)),
        }
    }
}

#[async_trait]
impl IReboot for RebootDouble {
    async fn save_context(&self, ctx: RebootContext) -> Result<()> {
        let mut saved = self.saved.write().await;
        saved.push(ctx);
        Ok(())
    }

    async fn load_context(&self) -> Result<Option<RebootContext>> {
        let mut saved = self.saved.write().await;
        Ok(saved.pop())
    }

    fn invoke(&self) -> Result<()> {
        self.invoked.swap(true, atomic::Ordering::SeqCst);
        Ok(())
    }
}
