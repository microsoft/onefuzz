use crate::failure::save_failure;
use backtrace::Backtrace;
use std::{panic, sync::Once};

fn panic_hook(info: &panic::PanicInfo) {
    let err = anyhow!("supervisor panicked: {}\n{:?}", info, Backtrace::new());
    if let Err(err) = save_failure(&err) {
        error!("unable to write panic log: {:?}", err);
    }
}

pub fn set_panic_handler() {
    static SET_HOOK: Once = Once::new();
    SET_HOOK.call_once(move || {
        let old_hook = panic::take_hook();
        panic::set_hook(Box::new(move |info| {
            panic_hook(&info);
            old_hook(info);
        }));
    });
}
