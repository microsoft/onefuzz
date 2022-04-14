// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![cfg(windows)]

// Allow safe functions that take `HANDLE` arguments.
//
// Though they type alias raw pointers, they are opaque. In the future, we will
// wrap them in a newtype. This will witness that they were obtained via win32
// API calls or documented pseudohandle construction.
#![allow(clippy::not_unsafe_ptr_arg_deref)]

mod breakpoint;
pub mod dbghelp;
mod debug_event;
mod debugger;
mod module;
pub mod stack;
mod target;

pub use self::{
    debug_event::DebugEvent,
    debugger::{BreakpointId, BreakpointType, DebugEventHandler, Debugger, ModuleLoadInfo},
};
