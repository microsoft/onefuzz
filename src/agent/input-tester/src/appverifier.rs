// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::useless_format)]
#![allow(clippy::upper_case_acronyms)]

use std::{
    env,
    ffi::{OsStr, OsString},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    str::FromStr,
    sync::{Arc, Mutex},
};

use anyhow::{Context, Result};
use log::debug;
use win_util::process;

use crate::logging;

/// The list of tests supported by Application Verifier
/// Run appverif.exe /? to see which tests are available.
#[derive(Clone, Copy, Debug)]
pub enum AppVerifierTest {
    Heaps,
    COM,
    RPC,
    Handles,
    Locks,
    Memory,
    TLS,
    Exceptions,
    DirtyStacks,
    LowRes,
    DangerousAPIs,
    TimeRollOver,
    Threadpool,
    Leak,
    SRWLock,
    HighVersionLie,
    LuaPriv,
    PrintAPI,
    PrintDriver,
    Networking,
    NTLMCaller,
    NTLMDowngrade,
    Webservices,
    Cuzz,
}

impl FromStr for AppVerifierTest {
    type Err = anyhow::Error;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s {
            "heaps" => Ok(AppVerifierTest::Heaps),
            "com" => Ok(AppVerifierTest::COM),
            "rpc" => Ok(AppVerifierTest::RPC),
            "handles" => Ok(AppVerifierTest::Handles),
            "locks" => Ok(AppVerifierTest::Locks),
            "memory" => Ok(AppVerifierTest::Memory),
            "tls" => Ok(AppVerifierTest::TLS),
            "exceptions" => Ok(AppVerifierTest::Exceptions),
            "dirtystacks" => Ok(AppVerifierTest::DirtyStacks),
            "lowres" => Ok(AppVerifierTest::LowRes),
            "dangerousapis" => Ok(AppVerifierTest::DangerousAPIs),
            "timerollover" => Ok(AppVerifierTest::TimeRollOver),
            "threadpool" => Ok(AppVerifierTest::Threadpool),
            "leak" => Ok(AppVerifierTest::Leak),
            "srwLock" => Ok(AppVerifierTest::SRWLock),
            "highversionlie" => Ok(AppVerifierTest::HighVersionLie),
            "luapriv" => Ok(AppVerifierTest::LuaPriv),
            "printapi" => Ok(AppVerifierTest::PrintAPI),
            "printdriver" => Ok(AppVerifierTest::PrintDriver),
            "networking" => Ok(AppVerifierTest::Networking),
            "ntlmcaller" => Ok(AppVerifierTest::NTLMCaller),
            "ntlmdowngrade" => Ok(AppVerifierTest::NTLMDowngrade),
            "webservices" => Ok(AppVerifierTest::Webservices),
            "cuzz" => Ok(AppVerifierTest::Cuzz),
            _ => Err(anyhow::anyhow!("Unknown appverif test")),
        }
    }
}

pub mod stop_codes {
    // The following is generated with a little PowerShell using the OS source (last edit Fri May  1 17:07:34 2015):
    // Unused codes were manually deleted.
    //
    // $stop_codes = Get-Content os\src\sdktools\debuggers\exts\badev\extdll\VerifierStops.cpp |
    //     Select-String '// DescriptionRoutine for APPLICATION_VERIFIER_(.*) \((0x.*)\)'
    // $stop_codes | Sort-Object -Property {[uint32]$_.matches.groups[2].ToString()} |
    //     ForEach-Object { "    pub const {0}: u32 = {1};" -f $_.matches.groups[1],$_.matches.groups[2] }
    //
    pub const HEAPS_UNKNOWN_ERROR: u32 = 0x1;
    pub const HEAPS_ACCESS_VIOLATION: u32 = 0x2;
    pub const HEAPS_UNSYNCHRONIZED_ACCESS: u32 = 0x3;
    pub const HEAPS_EXTREME_SIZE_REQUEST: u32 = 0x4;
    pub const HEAPS_BAD_HEAP_HANDLE: u32 = 0x5;
    pub const HEAPS_SWITCHED_HEAP_HANDLE: u32 = 0x6;
    pub const HEAPS_DOUBLE_FREE: u32 = 0x7;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK: u32 = 0x8;
    pub const HEAPS_DESTROY_PROCESS_HEAP: u32 = 0x9;
    pub const HEAPS_UNEXPECTED_EXCEPTION: u32 = 0xA;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_HEADER: u32 = 0xB;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_PROBING: u32 = 0xC;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_HEADER: u32 = 0xD;
    pub const HEAPS_CORRUPTED_FREED_HEAP_BLOCK: u32 = 0xE;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_SUFFIX: u32 = 0xF;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_START_STAMP: u32 = 0x10;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_END_STAMP: u32 = 0x11;
    pub const HEAPS_CORRUPTED_HEAP_BLOCK_PREFIX: u32 = 0x12;
    pub const HEAPS_FIRST_CHANCE_ACCESS_VIOLATION: u32 = 0x13;
    pub const HEAPS_CORRUPTED_HEAP_LIST: u32 = 0x14;
    pub const DANGEROUS_TERMINATE_THREAD_CALL: u32 = 0x100;
    pub const DANGEROUS_STACK_OVERFLOW: u32 = 0x101;
    pub const DANGEROUS_INVALID_EXIT_PROCESS_CALL: u32 = 0x102;
    pub const DANGEROUS_INVALID_LOAD_LIBRARY_CALL: u32 = 0x103;
    pub const DANGEROUS_INVALID_FREE_LIBRARY_CALL: u32 = 0x104;
    pub const DANGEROUS_INVALID_MINIMUM_PROCESS_WORKING_SIZE: u32 = 0x105;
    pub const DANGEROUS_INVALID_MAXIMUM_PROCESS_WORKING_SIZE: u32 = 0x106;
    pub const DANGEROUS_INVALID_MINIMUM_PROCESS_WORKING_SIZE_EX: u32 = 0x107;
    pub const DANGEROUS_INVALID_MAXIMUM_PROCESS_WORKING_SIZE_EX: u32 = 0x108;
    pub const LOCKS_EXIT_THREAD_OWNS_LOCK: u32 = 0x200;
    pub const LOCKS_LOCK_IN_UNLOADED_DLL: u32 = 0x201;
    pub const LOCKS_LOCK_IN_FREED_HEAP: u32 = 0x202;
    pub const LOCKS_LOCK_DOUBLE_INITIALIZE: u32 = 0x203;
    pub const LOCKS_LOCK_IN_FREED_MEMORY: u32 = 0x204;
    pub const LOCKS_LOCK_CORRUPTED: u32 = 0x205;
    pub const LOCKS_LOCK_INVALID_OWNER: u32 = 0x206;
    pub const LOCKS_LOCK_INVALID_RECURSION_COUNT: u32 = 0x207;
    pub const LOCKS_LOCK_INVALID_LOCK_COUNT: u32 = 0x208;
    pub const LOCKS_LOCK_OVER_RELEASED: u32 = 0x209;
    pub const LOCKS_LOCK_NOT_INITIALIZED: u32 = 0x210;
    pub const LOCKS_LOCK_ALREADY_INITIALIZED: u32 = 0x211;
    pub const LOCKS_LOCK_IN_FREED_VMEM: u32 = 0x212;
    pub const LOCKS_LOCK_IN_UNMAPPED_MEM: u32 = 0x213;
    pub const LOCKS_THREAD_NOT_LOCK_OWNER: u32 = 0x214;
    pub const LOCKS_LOCK_PRIVATE: u32 = 0x215;
    pub const SRWLOCK_NOT_INITIALIZED: u32 = 0x250;
    pub const SRWLOCK_ALREADY_INITIALIZED: u32 = 0x251;
    pub const SRWLOCK_MISMATCHED_ACQUIRE_RELEASE: u32 = 0x252;
    pub const SRWLOCK_RECURSIVE_ACQUIRE: u32 = 0x253;
    pub const SRWLOCK_EXIT_THREAD_OWNS_LOCK: u32 = 0x254;
    pub const SRWLOCK_INVALID_OWNER: u32 = 0x255;
    pub const SRWLOCK_IN_FREED_MEMORY: u32 = 0x256;
    pub const SRWLOCK_IN_UNLOADED_DLL: u32 = 0x257;
    pub const HANDLES_INVALID_HANDLE: u32 = 0x300;
    pub const HANDLES_INVALID_TLS_VALUE: u32 = 0x301;
    pub const HANDLES_INCORRECT_WAIT_CALL: u32 = 0x302;
    pub const HANDLES_NULL_HANDLE: u32 = 0x303;
    pub const HANDLES_WAIT_IN_DLLMAIN: u32 = 0x304;
    pub const HANDLES_INCORRECT_OBJECT_TYPE: u32 = 0x305;
    pub const TLS_TLS_LEAK: u32 = 0x350;
    pub const TLS_CORRUPTED_TLS: u32 = 0x351;
    pub const TLS_INVALID_TLS_INDEX: u32 = 0x352;
    pub const COM_ERROR: u32 = 0x400;
    pub const COM_API_IN_DLLMAIN: u32 = 0x401;
    pub const COM_UNHANDLED_EXCEPTION: u32 = 0x402;
    pub const COM_UNBALANCED_COINIT: u32 = 0x403;
    pub const COM_UNBALANCED_OLEINIT: u32 = 0x404;
    pub const COM_UNBALANCED_SWC: u32 = 0x405;
    pub const COM_NULL_DACL: u32 = 0x406;
    pub const COM_UNSAFE_IMPERSONATION: u32 = 0x407;
    pub const COM_SMUGGLED_WRAPPER: u32 = 0x408;
    pub const COM_SMUGGLED_PROXY: u32 = 0x409;
    pub const COM_CF_SUCCESS_WITH_NULL: u32 = 0x40A;
    pub const COM_GCO_SUCCESS_WITH_NULL: u32 = 0x40B;
    pub const COM_OBJECT_IN_FREED_MEMORY: u32 = 0x40C;
    pub const COM_OBJECT_IN_UNLOADED_DLL: u32 = 0x40D;
    pub const COM_VTBL_IN_FREED_MEMORY: u32 = 0x40E;
    pub const COM_VTBL_IN_UNLOADED_DLL: u32 = 0x40F;
    pub const COM_HOLDING_LOCKS_ON_CALL: u32 = 0x410;
    pub const COM_LOW_SECURITY_BLANKET_EXPLICIT: u32 = 0x413;
    pub const COM_LOW_SECURITY_BLANKET_REGISTRY: u32 = 0x414;
    pub const COM_LOW_SECURITY_BLANKET_PROXY: u32 = 0x415;
    pub const COM_LOW_SECURITY_BLANKET_SERVER: u32 = 0x416;
    pub const COM_SHOULD_CALL_OLEINITIALIZE: u32 = 0x417;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_SYNTAX: u32 = 0x418;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_ALL: u32 = 0x419;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_NO_IDENTIFYING_SUBSCRIBER_PROPERTIES: u32 = 0x41A;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_SELECTED_CONFIGURED_OBJECT: u32 = 0x41B;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_TRANSIENT_SUBSCRIPTION_ACCESS_DENIED: u32 = 0x41C;
    pub const COM_ES_IEVENTSYSTEM_REMOVE_USAGE_QUERY_SELECTS_MULTIPLE_OBJECTS: u32 = 0x41D;
    pub const COM_UNTRUSTED_MARSHALING: u32 = 0x421;
    pub const COM_UNEXPECTED_QUERYINTERFACE_ON_SERVER_SIDE_STANDARD_MARSHALER: u32 = 0x422;
    pub const COM_PREMATURE_STUB_RUNDOWN: u32 = 0x423;
    pub const COM_WINRT_ASYNC_OPERATION_COMPLETED_DELEGATE_SET_TO_NULL: u32 = 0x424;
    pub const COM_WINRT_ASYNC_OPERATION_COMPLETED_DELEGATE_DUPLICATE_SET_ATTEMPT: u32 = 0x425;
    pub const COM_WINRT_ASYNC_OPERATION_RELEASED_BEFORE_SETTING_COMPLETED_DELEGATE: u32 = 0x426;
    pub const COM_WINRT_ASYNC_OPERATION_RELEASED_BEFORE_GETTING_AVAILABLE_RESULTS: u32 = 0x427;
    pub const COM_WINRT_ASYNC_STATE_MACHINE_NOT_IN_TERMINAL_STATE: u32 = 0x428;
    pub const COM_WINRT_ASYNC_STATE_MACHINE_IN_CLOSED_STATE: u32 = 0x429;
    pub const COM_WINRT_ASYNC_OBJECT_MARSHALED_OUTOFPROC: u32 = 0x42A;
    pub const COM_WINRT_ASYNC_OPERATION_CLOSED_AFTER_CANCEL: u32 = 0x42B;
    pub const RPC_RPC_ERROR: u32 = 0x500;
    pub const MEMORY_INVALID_FREEMEM: u32 = 0x600;
    pub const MEMORY_INVALID_ALLOCMEM: u32 = 0x601;
    pub const MEMORY_INVALID_MAPVIEW: u32 = 0x602;
    pub const MEMORY_PROBE_INVALID_ADDRESS: u32 = 0x603;
    pub const MEMORY_PROBE_FREE_MEM: u32 = 0x604;
    pub const MEMORY_PROBE_GUARD_PAGE: u32 = 0x605;
    pub const MEMORY_PROBE_NULL: u32 = 0x606;
    pub const MEMORY_PROBE_INVALID_START_OR_SIZE: u32 = 0x607;
    pub const MEMORY_INVALID_DLL_RANGE: u32 = 0x608;
    pub const MEMORY_FREE_THREAD_STACK_MEMORY: u32 = 0x609;
    pub const MEMORY_INVALID_FREE_TYPE: u32 = 0x60A;
    pub const MEMORY_MEM_ALREADY_FREE: u32 = 0x60B;
    pub const MEMORY_INVALID_FREE_SIZE: u32 = 0x60C;
    pub const MEMORY_DLL_UNEXPECTED_EXCEPTION: u32 = 0x60D;
    pub const MEMORY_THREAD_UNEXPECTED_EXCEPTION: u32 = 0x60E;
    pub const MEMORY_PROBE_UNEXPECTED_EXCEPTION: u32 = 0x60F;
    pub const MEMORY_INVALID_MEM_RESET: u32 = 0x610;
    pub const MEMORY_FREE_THREAD_STACK_MEMORY_AS_HEAP: u32 = 0x612;
    pub const MEMORY_FREE_THREAD_STACK_MEMORY_AS_MAP: u32 = 0x613;
    pub const MEMORY_INVALID_RESOURCE_ADDRESS: u32 = 0x614;
    pub const MEMORY_INVALID_CRITSECT_ADDRESS: u32 = 0x615;
    pub const MEMORY_THREAD_UNEXPECTED_EXCEPTION_CODE: u32 = 0x616;
    pub const MEMORY_OUTBUFF_UNEXPECTED_EXCEPTION: u32 = 0x617;
    pub const MEMORY_SIZE_HEAP_UNEXPECTED_EXCEPTION: u32 = 0x618;
    pub const MEMORY_INVALID_FREEMEM_START_ADDRESS: u32 = 0x619;
    pub const MEMORY_INVALID_UNMAPVIEW_START_ADDRESS: u32 = 0x61A;
    pub const MEMORY_THREADPOOL_UNEXPECTED_EXCEPTION: u32 = 0x61B;
    pub const MEMORY_THREADPOOL_UNEXPECTED_EXCEPTION_CODE: u32 = 0x61C;
    pub const MEMORY_EXECUTABLE_HEAP: u32 = 0x61D;
    pub const MEMORY_EXECUTABLE_MEMORY: u32 = 0x61E;
    pub const EXCEPTIONS_FIRST_CHANCE_ACCESS_VIOLATION_CODE: u32 = 0x650;
    pub const THREADPOOL_INCONSISTENT_PRIORITY: u32 = 0x700;
    pub const THREADPOOL_INCONSISTENT_AFFINITY_MASK: u32 = 0x701;
    pub const THREADPOOL_ORPHANED_THREAD_MESSAGE: u32 = 0x702;
    pub const THREADPOOL_ORPHANED_THREAD_WINDOW: u32 = 0x703;
    pub const THREADPOOL_ILLEGAL_THREAD_EXIT: u32 = 0x704;
    pub const THREADPOOL_THREAD_IN_IMPERSONATION: u32 = 0x705;
    pub const THREADPOOL_PERSISTED_THREAD_NEEDED: u32 = 0x706;
    pub const THREADPOOL_DIRTY_TRANSACTION_CONTEXT: u32 = 0x707;
    pub const THREADPOOL_DIRTY_COM_STATE: u32 = 0x708;
    pub const THREADPOOL_INCONSISTENT_TIMER_PARAMS: u32 = 0x709;
    pub const THREADPOOL_LOADER_LOCK_HELD: u32 = 0x70A;
    pub const THREADPOOL_PREFERRED_LANGUAGES_SET: u32 = 0x70B;
    pub const THREADPOOL_BACKGROUND_PRIORITY_SET: u32 = 0x70C;
    pub const THREADPOOL_ILLEGAL_THREAD_TERMINATION: u32 = 0x70D;
    pub const LEAK_ALLOCATION: u32 = 0x900;
    pub const LEAK_HANDLE: u32 = 0x901;
    pub const LEAK_REGISTRY: u32 = 0x902;
    pub const LEAK_VIRTUAL_RESERVATION: u32 = 0x903;
    pub const LEAK_SYSSTRING: u32 = 0x904;
    pub const LEAK_POWER_NOTIFICATION: u32 = 0x905;
    pub const LEAK_COM_ALLOCATION: u32 = 0x906;
    pub const HIGHVERSIONLIE_GETVERSIONEX_API_SIMPLE: u32 = 0x2200;
    pub const HIGHVERSIONLIE_GETVERSIONEX_API_SIMPLE_CSD: u32 = 0x2201;
    pub const HIGHVERSIONLIE_GETVERSIONEX_API_EXTENDED: u32 = 0x2202;
    pub const HIGHVERSIONLIE_GETVERSIONEX_API_EXTENDED_CSD: u32 = 0x2203;
    pub const HIGHVERSIONLIE_GETVERSION_API_CALL: u32 = 0x2204;
    pub const LUAPRIV_CANNOTQUERYOBJECT: u32 = 0x3300;
    pub const LUAPRIV_CANTCANONICALIZEPATH: u32 = 0x3301;
    pub const LUAPRIV_CANTOPEN_NONCRITICAL: u32 = 0x3302;
    pub const LUAPRIV_BADHKCU: u32 = 0x3303;
    pub const LUAPRIV_NO_USERPROFILE: u32 = 0x3304;
    pub const LUAPRIV_OK_OBJECT_PREFIX: u32 = 0x3305;
    pub const LUAPRIV_RESTRICTED_NAMESPACE: u32 = 0x3306;
    pub const LUAPRIV_NO_NAMESPACE: u32 = 0x3307;
    pub const LUAPRIV_CANTGETPARENT: u32 = 0x3308;
    pub const LUAPRIV_CANT_OPEN_PARENT: u32 = 0x3309;
    pub const LUAPRIV_NON_LUA_USER: u32 = 0x330A;
    pub const LUAPRIV_STRING2SID_FAILED: u32 = 0x330B;
    pub const LUAPRIV_GETTOKENINFO: u32 = 0x330C;
    pub const LUAPRIV_UNKNOWN_PRIVILEGE: u32 = 0x330D;
    pub const LUAPRIV_PRIV_LOOKUP_FAILED: u32 = 0x330E;
    pub const LUAPRIV_USED_PRIVILEGE_LUID: u32 = 0x330F;
    pub const LUAPRIV_FAILED_PRIVILEGE_LUID: u32 = 0x3310;
    pub const LUAPRIV_PRIVILEGED_USER: u32 = 0x3311;
    pub const LUAPRIV_IRRELEVANT_PRIVILEGE_DENIED: u32 = 0x3312;
    pub const LUAPRIV_IRRELEVANT_UNKNOWN_PRIVILEGE_DENIED: u32 = 0x3313;
    pub const LUAPRIV_CANT_QUERY_VALUE: u32 = 0x3314;
    pub const LUAPRIV_UNKNOWN_MAPPING: u32 = 0x3315;
    pub const LUAPRIV_INI_PROFILE_ACCESS_DENIED: u32 = 0x3316;
    pub const LUAPRIV_OK_OBJECT_DUMP: u32 = 0x3317;
    pub const LUAPRIV_BAD_OBJECT_DUMP: u32 = 0x3318;
    pub const LUAPRIV_SD2TEXT: u32 = 0x3319;
    pub const LUAPRIV_DENY_ACE: u32 = 0x331A;
    pub const LUAPRIV_RESTRICTED_RIGHT: u32 = 0x331B;
    pub const LUAPRIV_RESTRICTED_RIGHT_MORE: u32 = 0x331C;
    pub const LUAPRIV_CREATOR_OWNER: u32 = 0x331D;
    pub const LUAPRIV_OK_OBJECT_GRANT: u32 = 0x331E;
    pub const LUAPRIV_EMPTY_DACL: u32 = 0x331F;
    pub const LUAPRIV_MISSING_PIECE: u32 = 0x3320;
    pub const LUAPRIV_MISSING_ACE: u32 = 0x3321;
    pub const LUAPRIV_MAXIMUM_ALLOWED: u32 = 0x3322;
    pub const LUAPRIV_UNKNOWN_MAXIMUM_ALLOWED: u32 = 0x3323;
    pub const LUAPRIV_UNKNOWN_PERMS: u32 = 0x3324;
    pub const LUAPRIV_INI_PROFILE_ACCESS_GRANTED: u32 = 0x3325;
    pub const LUAPRIV_CHECKTOKENMEMBERSHIP_TRUSTED: u32 = 0x3326;
    pub const LUAPRIV_CHECKTOKENMEMBERSHIP_UNTRUSTED: u32 = 0x3327;
    pub const LUAPRIV_INI_PROFILE_CONCERN: u32 = 0x3328;
    pub const LUAPRIV_OP_REQUIRES_ACCESS: u32 = 0x3329;
    pub const LUAPRIV_CANNOT_QUERY_ACCESS: u32 = 0x332A;
    pub const LUAPRIV_ELEVATION_REQUIRED: u32 = 0x332B;
    pub const LUAPRIV_ELEVATION_DETECTED: u32 = 0x332C;
    pub const LUAPRIV_OBJECT_INACCESSIBLE: u32 = 0x332D;
    pub const LUAPRIV_FAILED_API_CALL: u32 = 0x332E;
    pub const LUAPRIV_SECURITY_LOG_OPENED: u32 = 0x332F;
    pub const LUAPRIV_INI_PROFILE_FAILED: u32 = 0x3330;
    pub const LUAPRIV_VIRTUALIZED_DELETION: u32 = 0x3331;
    pub const LUAPRIV_UNKNOWN_API_OPTIONS: u32 = 0x3332;
    pub const LUAPRIV_SET_GLOBAL_HOOK: u32 = 0x3333;
    pub const LUAPRIV_SET_HOOK_FAILED: u32 = 0x3334;
    pub const LUAPRIV_NETUSERGETINFO: u32 = 0x3335;
    pub const LUAPRIV_SETACTIVEPWRSCHEME: u32 = 0x3336;
    pub const LUAPRIV_SETACTIVEPWRSCHEME_FAILED: u32 = 0x3337;
    pub const LUAPRIV_ACCESSCHECK: u32 = 0x3338;
    pub const LUAPRIV_HARDADMINCHECK: u32 = 0x3339;
    pub const LUAPRIV_FILE_NAME: u32 = 0x333A;
    pub const LUAPRIV_FILE_VERSION: u32 = 0x333B;
    pub const LUAPRIV_FILE_PRODUCT_VERSION: u32 = 0x333C;
    pub const LUAPRIV_FILE_DESCRIPTION: u32 = 0x333D;
    pub const LUAPRIV_FILE_PRODUCT_NAME: u32 = 0x333E;
    pub const LUAPRIV_FILE_COMPANY_NAME: u32 = 0x333F;
    pub const LUAPRIV_FILE_ORIGINAL_FILENAME: u32 = 0x3340;
    pub const LUAPRIV_RESTRICTED_BY_MIC: u32 = 0x3341;
    pub const LUAPRIV_LUAPRIV_VERSION: u32 = 0x33FF;
    pub const NTLMCALLER_ACH_EXPLICIT_NTLM_PACKAGE: u32 = 0x5000;
    pub const NTLMCALLER_ACH_IMPLICITLY_USE_NTLM: u32 = 0x5001;
    pub const NTLMCALLER_ACH_BAD_NTLM_EXCLUSION: u32 = 0x5002;
    pub const NTLMCALLER_ISC_MALFORMED_TARGET: u32 = 0x5003;
    pub const NTLMDOWNGRADE_FALLBACK_TO_NTLM: u32 = 0x5010;
    pub const WEBSERVICES_INVALID_OBJECT_ADDRESS: u32 = 0x6000;
    pub const WEBSERVICES_SINGLE_THREADED_OBJECT_VIOLATION: u32 = 0x6001;
    pub const WEBSERVICES_OBJECT_IN_USE: u32 = 0x6002;
    pub const WEBSERVICES_API_TIMEOUT: u32 = 0x6003;
    pub const WEBSERVICES_CORRUPT_CALL_CONTEXT: u32 = 0x6004;
    pub const PRINTAPI_LEAKED_PRINTER_HANDLE: u32 = 0xA000;
    pub const PRINTAPI_LEAKED_PRINTER_CHANGE_NOTIFICATION_HANDLE: u32 = 0xA001;
    pub const PRINTAPI_LEAKED_PPRINTER_NOTIFY_INFO: u32 = 0xA002;
    pub const PRINTAPI_MULTITHREADED_ACCESS_TO_PRINTER_HANDLE: u32 = 0xA003;
    pub const PRINTAPI_PRINTER_HANDLE_ACCESSED_NOT_ON_THE_THREAD_THAT_OPENED_IT: u32 = 0xA004;
    pub const PRINTAPI_PRINTER_HANDLE_ALREADY_CLOSED: u32 = 0xA005;
    pub const PRINTAPI_INVALID_PRINTER_HANDLE: u32 = 0xA006;
    pub const PRINTAPI_PRINTER_CHANGE_NOTIFICATION_HANDLE_ALREADY_CLOSED: u32 = 0xA007;
    pub const PRINTAPI_UNKNOWN_PRINTER_CHANGE_NOTIFICATION_HANDLE: u32 = 0xA008;
    pub const PRINTAPI_PRINTER_NOTIFY_INFO_ALREADY_FREED: u32 = 0xA009;
    pub const PRINTAPI_INVALID_PRINTER_NOTIFY_INFO: u32 = 0xA00A;
    pub const PRINTAPI_TOO_MANY_OPENED_PRINTER_HANDLES: u32 = 0xA00B;
    pub const PRINTAPI_WINSPOOL_OPENPRINTER2W_EXPORTED_ON_PRE_VISTA_OS: u32 = 0xA00C;
    pub const PRINTAPI_TOO_MANY_OPENED_PRINT_TICKET_PROVIDER_HANDLES: u32 = 0xA00D;
    pub const PRINTAPI_PRINT_TICKET_PROVIDER_HANDLE_ALREADY_CLOSED: u32 = 0xA00E;
    pub const PRINTAPI_UNKNOWN_PRINT_TICKET_PROVIDER_HANDLE: u32 = 0xA00F;
    pub const PRINTAPI_MULTITHREADED_ACCESS_TO_PRINT_TICKET_PROVIDER_HANDLE: u32 = 0xA010;
    pub const PRINTAPI_PRINT_TICKET_PROVIDER_HANDLE_ACCESSED_NOT_ON_THE_THREAD_THAT_OPENED_IT: u32 =
        0xA011;
    pub const PRINTAPI_LEAKED_PRINT_TICKET_PROVIDER_HANDLE: u32 = 0xA012;
    pub const PRINTAPI_TOO_MANY_OPENED_PRINTER_CHANGE_NOTIFICATION_HANDLES: u32 = 0xA013;
    pub const PRINTAPI_TOO_MANY_OPENED_PRINTER_NOTIFY_INFO_OBJECTS: u32 = 0xA014;
    pub const PRINTAPI_INVALID_APPLICATION_PRINTTICKET: u32 = 0xA015;
    pub const PRINTAPI_INVALID_APPLICATION_PRINTCAPABILITIES: u32 = 0xA016;
    pub const PRINTAPI_PRINTTICKET_API_INVALID_NULL_ARGUMENT: u32 = 0xA017;
    pub const PRINTAPI_PTCONFORM_UNEXPECTED_ERROR: u32 = 0xA018;
    pub const PRINTAPI_UNSUPPORTED_API_CALL_IN_DLLMAIN: u32 = 0xA019;
    pub const PRINTAPI_LEAKED_SPOOL_FILE_HANDLE: u32 = 0xA01A;
    pub const PRINTAPI_SPOOL_FILE_HANDLE_ALREADY_CLOSED: u32 = 0xA01B;
    pub const PRINTAPI_INVALID_SPOOL_FILE_HANDLE: u32 = 0xA01C;
    pub const PRINTAPI_TOO_MANY_OPENED_SPOOL_FILE_HANDLES: u32 = 0xA01D;
    pub const PRINTAPI_DEVMODE_BUFFER_SPANS_IN_NON_READABLE_MEMORY_PAGE: u32 = 0xA01E;
    pub const PRINTAPI_MODULE_UNLOAD: u32 = 0xA01F;
    pub const PRINTAPI_LEAKED_ASYNC_NOTIFY_HANDLE: u32 = 0xA020;
    pub const PRINTAPI_INVALID_ASYNC_NOTIFY_HANDLE: u32 = 0xA021;
    pub const PRINTAPI_ASYNC_NOTIFY_HANDLE_ALREADY_CLOSED: u32 = 0xA022;
    pub const PRINTAPI_REFCOUNT_PLUS_AFTER_FAIL: u32 = 0xA023;
    pub const PRINTAPI_REFCOUNT_PLUS_AFTER_API_FAIL: u32 = 0xA024;
    pub const PRINTAPI_ASYNCCHANNEL_OS_CONTRACT_VIOLATION: u32 = 0xA025;
    pub const PRINTAPI_ASYNCCHANNEL_CLIENT_CONTRACT_VIOLATION: u32 = 0xA026;
    pub const PRINTAPI_ASYNCCHANNEL_CLOSECHANNEL_RACE_DETECTED: u32 = 0xA027;
    pub const PRINTAPI_CALLING_NETBOUND_PRINT_API_ON_GUI_THREAD: u32 = 0xA028;
    pub const PRINTAPI_UNSUPPORTED_API_CALLED_IN_SESSION_ZERO: u32 = 0xA029;
    pub const PRINTDRIVER_FIRST_CHANCE_ACCESS_VIOLATION: u32 = 0xD000;
    pub const PRINTDRIVER_INT_DIVIDE_BY_ZERO: u32 = 0xD001;
    pub const PRINTDRIVER_DATATYPE_MISALIGNMENT: u32 = 0xD002;
    pub const PRINTDRIVER_INVALID_HANDLE: u32 = 0xD003;
    pub const PRINTDRIVER_PRINTER_HANDLE_ALREADY_CLOSED: u32 = 0xD004;
    pub const PRINTDRIVER_INVALID_PRINTER_HANDLE: u32 = 0xD005;
    pub const PRINTDRIVER_PLUGIN_CLOSED_PRINTER_HANDLE: u32 = 0xD006;
    pub const PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_NUMBER_OF_SUPPORTED_SCHEMA_VERSIONS: u32 =
        0xD007;
    pub const PRINTDRIVER_PRINTTICKET_PROVIDER_MISSING_SUPPORTED_SCHEMA_VERSION: u32 = 0xD008;
    pub const PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_SUPPORTED_SCHEMA_VERSION: u32 = 0xD009;
    pub const PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_OEMPTOPTS: u32 = 0xD00A;
    pub const PRINTDRIVER_PRINTTICKET_PROVIDER_MISSING_NAMESPACE: u32 = 0xD00B;
    pub const PRINTDRIVER_PLUGIN_MISMATCHED_REFCOUNT: u32 = 0xD00C;
    pub const PRINTDRIVER_PPTL_IS_NULL_IN_OEMNEXTBAND: u32 = 0xD00D;
    pub const PRINTDRIVER_PLUGIN_PRIVATE_PDEV_IS_NULL: u32 = 0xD00E;
    pub const PRINTDRIVER_INVALID_PLUGIN_PRIVATE_DEVMODE_SIZE: u32 = 0xD00F;
    pub const PRINTDRIVER_PLUGIN_PRIVATE_DEVMODE_MISMATCHED_SIZE: u32 = 0xD010;
    pub const PRINTDRIVER_INVALID_PLUGIN_SIGNATURE: u32 = 0xD011;
    pub const PRINTDRIVER_PLUGIN_PRIVATE_DEVMODE_MISMATCHED_SIGNATURE: u32 = 0xD012;
    pub const PRINTDRIVER_ENABLEDRIVER_FAILED: u32 = 0xD013;
    pub const PRINTDRIVER_ENABLEDRIVER_FAILED_WITHOUT_ERROR_CODE: u32 = 0xD014;
    pub const PRINTDRIVER_INVALID_SETBANDSIZE_CALL: u32 = 0xD015;
    pub const PRINTDRIVER_INVALID_WRITEPRINTER_INITIALIZATION_CALL: u32 = 0xD016;
    pub const PRINTDRIVER_WRITEPRINTER_FAILED: u32 = 0xD017;
    pub const PRINTDRIVER_INVALID_COREDRIVER_PRINTTICKET: u32 = 0xD018;
    pub const PRINTDRIVER_INVALID_PLUGIN_PRINTTICKET: u32 = 0xD019;
    pub const PRINTDRIVER_INVALID_COREDRIVER_PRINTCAPABILITIES: u32 = 0xD01A;
    pub const PRINTDRIVER_INVALID_PLUGIN_PRINTCAPABILITIES: u32 = 0xD01B;
    pub const PRINTDRIVER_PTCONFORM_UNEXPECTED_ERROR: u32 = 0xD01C;
    pub const PRINTDRIVER_FILTER_INVALID_ARGUMENT: u32 = 0xD01D;
    pub const PRINTDRIVER_FILTER_PROPERTY_BAG_INVALID_CHANGE: u32 = 0xD01E;
    pub const PRINTDRIVER_FILTER_INVALID_CALL_ORDER: u32 = 0xD01F;
    pub const PRINTDRIVER_FILTER_REFCOUNT_MISMATCH: u32 = 0xD020;
    pub const PRINTDRIVER_FILTER_UNEXPECTED_CALL: u32 = 0xD021;
    pub const PRINTDRIVER_PIPELINE_INVALID_CALL_ORDER: u32 = 0xD022;
    pub const PRINTDRIVER_PIPELINE_INVALID_INPUT_ARGUMENT: u32 = 0xD023;
    pub const PRINTDRIVER_PIPELINE_INVALID_OUTPUT_ARGUMENT: u32 = 0xD024;
    pub const PRINTDRIVER_SECURITY_CONTEXT_CHANGED_BY_A_PRINT_DRIVER_CALL: u32 = 0xD025;
    pub const PRINTDRIVER_INVALID_FILTER_PRINTTICKET: u32 = 0xD026;
    pub const PRINTDRIVER_INVALID_PIPELINE_PRINTTICKET: u32 = 0xD027;
    pub const PRINTDRIVER_DLL_PREMATURE_UNLOAD: u32 = 0xD028;
    pub const PRINTDRIVER_COM_INTERFACE_ALREADY_RELEASED: u32 = 0xD029;
    pub const PRINTDRIVER_DRIVER_CALLED_EXITTHREAD: u32 = 0xD02A;
    pub const PRINTDRIVER_DRIVER_CALLED_TERMINATETHREAD: u32 = 0xD02B;
    pub const PRINTDRIVER_COM_APARTMENT_TYPE_CHANGED: u32 = 0xD02C;
    pub const PRINTDRIVER_COM_NOT_INITIALIZED: u32 = 0xD02D;
    pub const PRINTDRIVER_XML_DOM_REFCOUNT_CHANGED: u32 = 0xD02E;
    pub const PRINTDRIVER_FATALEXIT: u32 = 0xD02F;
    pub const NETWORKING_UNSUPPORTED_API_CALL_IN_DLLMAIN: u32 = 0xE100;
    pub const NETWORKING_WSA_SOCKET_ALREADY_CLOSED: u32 = 0xE101;
    pub const NETWORKING_WSA_INVALID_SOCKET_HANDLE: u32 = 0xE102;
    pub const NETWORKING_WSA_LEAKED_SOCKET_HANDLE: u32 = 0xE103;
    pub const NETWORKING_WSP_SOCKET_ALREADY_CLOSED: u32 = 0xE104;
    pub const NETWORKING_WSP_INVALID_SOCKET_HANDLE: u32 = 0xE105;
    pub const NETWORKING_WSP_LEAKED_SOCKET_HANDLE: u32 = 0xE106;
    pub const NETWORKING_WSA_NOT_INITIALIZED: u32 = 0xE107;
    pub const NETWORKING_WSP_NOT_INITIALIZED: u32 = 0xE108;
    pub const NETWORKING_NSP_NOT_INITIALIZED: u32 = 0xE109;
    pub const NETWORKING_INVALID_FUNCTION_POINTER_DETECTED: u32 = 0xE10A;
    pub const NETWORKING_WSA_SOCKETS_ABORTED: u32 = 0xE10B;
    pub const NETWORKING_WSP_SOCKETS_ABORTED: u32 = 0xE10C;
    pub const NETWORKING_WSA_RETURN_INVALID: u32 = 0xE10D;
    pub const NETWORKING_WSP_RETURN_INVALID: u32 = 0xE10E;
    pub const CUZZ_DATA_RACE: u32 = 0xF000;
}

/// The stop codes are documented in the help file for appverif.exe.
/// Run appverif.exe, then press F1 to see the help.
fn stop_codes(test: AppVerifierTest) -> &'static [u32] {
    match test {
        AppVerifierTest::Heaps => &[
            stop_codes::HEAPS_UNKNOWN_ERROR,         // Unknown Error.
            stop_codes::HEAPS_ACCESS_VIOLATION,      // Access Violation Exception.
            stop_codes::HEAPS_UNSYNCHRONIZED_ACCESS, // Multithreaded access in a heap created with HEAP_NO_SERIALIZE.
            stop_codes::HEAPS_EXTREME_SIZE_REQUEST,  // Extreme size request.
            stop_codes::HEAPS_BAD_HEAP_HANDLE, // Heap handle with incorrect signature.
            stop_codes::HEAPS_SWITCHED_HEAP_HANDLE, // Corrupted heap pointer or using wrong heap.
            stop_codes::HEAPS_DOUBLE_FREE,          // Heap block already freed.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK, // Corrupted heap block.
            stop_codes::HEAPS_DESTROY_PROCESS_HEAP, // Attempt to destroy process heap.
            stop_codes::HEAPS_UNEXPECTED_EXCEPTION, // Unexpected exception raised while executing heap management code.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_HEADER, // Exception raised while verifying heap block header.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_EXCEPTION_RAISED_FOR_PROBING, // Exception raised while verifying the heap block.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_HEADER, // Heap block corrupted after being freed.
            stop_codes::HEAPS_CORRUPTED_FREED_HEAP_BLOCK, // Corrupted infix pattern for freed heap block.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_SUFFIX, // Corrupted suffix pattern for heap block.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_START_STAMP, // Corrupted start stamp for heap block.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_END_STAMP, // Corrupted end stamp for heap block.
            stop_codes::HEAPS_CORRUPTED_HEAP_BLOCK_PREFIX, // Corrupted prefix pattern for heap block.
            stop_codes::HEAPS_FIRST_CHANCE_ACCESS_VIOLATION, // First chance access violation for current stack trace.
            stop_codes::HEAPS_CORRUPTED_HEAP_LIST, // Invalid process heap list count.
        ],
        AppVerifierTest::COM => &[
            stop_codes::COM_ERROR,
            stop_codes::COM_API_IN_DLLMAIN,
            stop_codes::COM_UNHANDLED_EXCEPTION,
            stop_codes::COM_UNBALANCED_COINIT,
            stop_codes::COM_UNBALANCED_OLEINIT,
            stop_codes::COM_UNBALANCED_SWC,
            stop_codes::COM_NULL_DACL,
            stop_codes::COM_UNSAFE_IMPERSONATION,
            stop_codes::COM_SMUGGLED_WRAPPER,
            stop_codes::COM_SMUGGLED_PROXY,
            stop_codes::COM_CF_SUCCESS_WITH_NULL,
            stop_codes::COM_GCO_SUCCESS_WITH_NULL,
            stop_codes::COM_OBJECT_IN_FREED_MEMORY,
            stop_codes::COM_OBJECT_IN_UNLOADED_DLL,
            stop_codes::COM_VTBL_IN_FREED_MEMORY,
            stop_codes::COM_VTBL_IN_UNLOADED_DLL,
            stop_codes::COM_HOLDING_LOCKS_ON_CALL,
            stop_codes::COM_LOW_SECURITY_BLANKET_EXPLICIT,
            stop_codes::COM_LOW_SECURITY_BLANKET_REGISTRY,
            stop_codes::COM_LOW_SECURITY_BLANKET_PROXY,
            stop_codes::COM_LOW_SECURITY_BLANKET_SERVER,
            stop_codes::COM_SHOULD_CALL_OLEINITIALIZE,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_SYNTAX,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_ALL,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_NO_IDENTIFYING_SUBSCRIBER_PROPERTIES,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_SELECTED_CONFIGURED_OBJECT,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_TRANSIENT_SUBSCRIPTION_ACCESS_DENIED,
            stop_codes::COM_ES_IEVENTSYSTEM_REMOVE_USAGE_QUERY_SELECTS_MULTIPLE_OBJECTS,
            stop_codes::COM_UNTRUSTED_MARSHALING,
            stop_codes::COM_UNEXPECTED_QUERYINTERFACE_ON_SERVER_SIDE_STANDARD_MARSHALER,
            stop_codes::COM_PREMATURE_STUB_RUNDOWN,
            stop_codes::COM_WINRT_ASYNC_OPERATION_COMPLETED_DELEGATE_SET_TO_NULL,
            stop_codes::COM_WINRT_ASYNC_OPERATION_COMPLETED_DELEGATE_DUPLICATE_SET_ATTEMPT,
            stop_codes::COM_WINRT_ASYNC_OPERATION_RELEASED_BEFORE_SETTING_COMPLETED_DELEGATE,
            stop_codes::COM_WINRT_ASYNC_OPERATION_RELEASED_BEFORE_GETTING_AVAILABLE_RESULTS,
            stop_codes::COM_WINRT_ASYNC_STATE_MACHINE_NOT_IN_TERMINAL_STATE,
            stop_codes::COM_WINRT_ASYNC_STATE_MACHINE_IN_CLOSED_STATE,
            stop_codes::COM_WINRT_ASYNC_OBJECT_MARSHALED_OUTOFPROC,
            stop_codes::COM_WINRT_ASYNC_OPERATION_CLOSED_AFTER_CANCEL,
        ],
        AppVerifierTest::RPC => &[
            stop_codes::RPC_RPC_ERROR,
        ],
        AppVerifierTest::Handles => &[
            stop_codes::HANDLES_INVALID_HANDLE, // Invalid handle exception for current stack trace.
            stop_codes::HANDLES_INVALID_TLS_VALUE, // Invalid TLS index used for current stack trace.
            stop_codes::HANDLES_INCORRECT_WAIT_CALL, // Invalid parameters for WaitForMultipleObjects call.
            stop_codes::HANDLES_NULL_HANDLE,         // NULL handle passed as parameter.
            stop_codes::HANDLES_WAIT_IN_DLLMAIN, // Waiting on a thread handle in DllMain.
            stop_codes::HANDLES_INCORRECT_OBJECT_TYPE, // Incorrect object type for handle.
        ],
        AppVerifierTest::Locks => &[
            stop_codes::LOCKS_EXIT_THREAD_OWNS_LOCK,
            stop_codes::LOCKS_LOCK_IN_UNLOADED_DLL,
            stop_codes::LOCKS_LOCK_IN_FREED_HEAP,
            stop_codes::LOCKS_LOCK_DOUBLE_INITIALIZE,
            stop_codes::LOCKS_LOCK_IN_FREED_MEMORY,
            stop_codes::LOCKS_LOCK_CORRUPTED,
            stop_codes::LOCKS_LOCK_INVALID_OWNER,
            stop_codes::LOCKS_LOCK_INVALID_RECURSION_COUNT,
            stop_codes::LOCKS_LOCK_INVALID_LOCK_COUNT,
            stop_codes::LOCKS_LOCK_OVER_RELEASED,
            stop_codes::LOCKS_LOCK_NOT_INITIALIZED,
            stop_codes::LOCKS_LOCK_ALREADY_INITIALIZED,
            stop_codes::LOCKS_LOCK_IN_FREED_VMEM,
            stop_codes::LOCKS_LOCK_IN_UNMAPPED_MEM,
            stop_codes::LOCKS_THREAD_NOT_LOCK_OWNER,
            stop_codes::LOCKS_LOCK_PRIVATE,
        ],
        AppVerifierTest::Memory => &[
            stop_codes::MEMORY_INVALID_FREEMEM,
            stop_codes::MEMORY_INVALID_ALLOCMEM,
            stop_codes::MEMORY_INVALID_MAPVIEW,
            stop_codes::MEMORY_PROBE_INVALID_ADDRESS,
            stop_codes::MEMORY_PROBE_FREE_MEM,
            stop_codes::MEMORY_PROBE_GUARD_PAGE,
            stop_codes::MEMORY_PROBE_NULL,
            stop_codes::MEMORY_PROBE_INVALID_START_OR_SIZE,
            stop_codes::MEMORY_INVALID_DLL_RANGE,
            stop_codes::MEMORY_FREE_THREAD_STACK_MEMORY,
            stop_codes::MEMORY_INVALID_FREE_TYPE,
            stop_codes::MEMORY_MEM_ALREADY_FREE,
            stop_codes::MEMORY_INVALID_FREE_SIZE,
            stop_codes::MEMORY_DLL_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_THREAD_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_PROBE_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_INVALID_MEM_RESET,
            stop_codes::MEMORY_FREE_THREAD_STACK_MEMORY_AS_HEAP,
            stop_codes::MEMORY_FREE_THREAD_STACK_MEMORY_AS_MAP,
            stop_codes::MEMORY_INVALID_RESOURCE_ADDRESS,
            stop_codes::MEMORY_INVALID_CRITSECT_ADDRESS,
            stop_codes::MEMORY_THREAD_UNEXPECTED_EXCEPTION_CODE,
            stop_codes::MEMORY_OUTBUFF_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_SIZE_HEAP_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_INVALID_FREEMEM_START_ADDRESS,
            stop_codes::MEMORY_INVALID_UNMAPVIEW_START_ADDRESS,
            stop_codes::MEMORY_THREADPOOL_UNEXPECTED_EXCEPTION,
            stop_codes::MEMORY_THREADPOOL_UNEXPECTED_EXCEPTION_CODE,
            stop_codes::MEMORY_EXECUTABLE_HEAP,
            stop_codes::MEMORY_EXECUTABLE_MEMORY,
        ],
        AppVerifierTest::TLS => &[
            stop_codes::TLS_TLS_LEAK,
            stop_codes::TLS_CORRUPTED_TLS,
            stop_codes::TLS_INVALID_TLS_INDEX,
        ],
        AppVerifierTest::Exceptions => &[
            stop_codes::EXCEPTIONS_FIRST_CHANCE_ACCESS_VIOLATION_CODE, // Attempt to execute code in non-executable memory.
        ],
        // DirtyStacks doesn't have any stop codes - but it does write garbage to unused stack
        // periodically in an attempt to detect uninitialized variables.
        // It might not be deterministic, so it might result in bugs that don't repro.
        AppVerifierTest::DirtyStacks => &[],
        // LowRes is used for fault injection and doesn't have any stop codes.
        // It might not be deterministic, so it might result in bugs that don't repro.
        AppVerifierTest::LowRes => &[],
        AppVerifierTest::DangerousAPIs => &[
            stop_codes::DANGEROUS_TERMINATE_THREAD_CALL,
            stop_codes::DANGEROUS_STACK_OVERFLOW,
            stop_codes::DANGEROUS_INVALID_EXIT_PROCESS_CALL,
            stop_codes::DANGEROUS_INVALID_LOAD_LIBRARY_CALL,
            stop_codes::DANGEROUS_INVALID_FREE_LIBRARY_CALL,
            stop_codes::DANGEROUS_INVALID_MINIMUM_PROCESS_WORKING_SIZE,
            stop_codes::DANGEROUS_INVALID_MAXIMUM_PROCESS_WORKING_SIZE,
            stop_codes::DANGEROUS_INVALID_MINIMUM_PROCESS_WORKING_SIZE_EX,
            stop_codes::DANGEROUS_INVALID_MAXIMUM_PROCESS_WORKING_SIZE_EX,
        ],
        // TimeRollOver doesn't have any stop codes - it causes GetTickCount and TimeGetTime to
        // roll over faster than they would normally.
        AppVerifierTest::TimeRollOver => &[],
        AppVerifierTest::Threadpool => &[
            stop_codes::THREADPOOL_INCONSISTENT_PRIORITY,
            stop_codes::THREADPOOL_INCONSISTENT_AFFINITY_MASK,
            stop_codes::THREADPOOL_ORPHANED_THREAD_MESSAGE,
            stop_codes::THREADPOOL_ORPHANED_THREAD_WINDOW,
            stop_codes::THREADPOOL_ILLEGAL_THREAD_EXIT,
            stop_codes::THREADPOOL_THREAD_IN_IMPERSONATION,
            stop_codes::THREADPOOL_PERSISTED_THREAD_NEEDED,
            stop_codes::THREADPOOL_DIRTY_TRANSACTION_CONTEXT,
            stop_codes::THREADPOOL_DIRTY_COM_STATE,
            stop_codes::THREADPOOL_INCONSISTENT_TIMER_PARAMS,
            stop_codes::THREADPOOL_LOADER_LOCK_HELD,
            stop_codes::THREADPOOL_PREFERRED_LANGUAGES_SET,
            stop_codes::THREADPOOL_BACKGROUND_PRIORITY_SET,
            stop_codes::THREADPOOL_ILLEGAL_THREAD_TERMINATION,
        ],
        AppVerifierTest::Leak => &[
            stop_codes::LEAK_ALLOCATION, // A heap allocation was leaked.
            stop_codes::LEAK_HANDLE,     // A handle was leaked.
            stop_codes::LEAK_REGISTRY,   // An HKEY was leaked.
            stop_codes::LEAK_VIRTUAL_RESERVATION, // A virtual reservation was leaked.
            stop_codes::LEAK_SYSSTRING,  // A BSTR was leaked.
            stop_codes::LEAK_POWER_NOTIFICATION, // A power notification was not unregistered.
        ],
        AppVerifierTest::SRWLock => &[
            stop_codes::SRWLOCK_NOT_INITIALIZED,
            stop_codes::SRWLOCK_ALREADY_INITIALIZED,
            stop_codes::SRWLOCK_MISMATCHED_ACQUIRE_RELEASE,
            stop_codes::SRWLOCK_RECURSIVE_ACQUIRE,
            stop_codes::SRWLOCK_EXIT_THREAD_OWNS_LOCK,
            stop_codes::SRWLOCK_INVALID_OWNER,
            stop_codes::SRWLOCK_IN_FREED_MEMORY,
            stop_codes::SRWLOCK_IN_UNLOADED_DLL,
        ],
        AppVerifierTest::HighVersionLie => &[
            stop_codes::HIGHVERSIONLIE_GETVERSIONEX_API_SIMPLE,
            stop_codes::HIGHVERSIONLIE_GETVERSIONEX_API_SIMPLE_CSD,
            stop_codes::HIGHVERSIONLIE_GETVERSIONEX_API_EXTENDED,
            stop_codes::HIGHVERSIONLIE_GETVERSIONEX_API_EXTENDED_CSD,
            stop_codes::HIGHVERSIONLIE_GETVERSION_API_CALL,
        ],
        AppVerifierTest::LuaPriv => &[
            stop_codes::LUAPRIV_CANNOTQUERYOBJECT,
            stop_codes::LUAPRIV_CANTCANONICALIZEPATH,
            stop_codes::LUAPRIV_CANTOPEN_NONCRITICAL,
            stop_codes::LUAPRIV_BADHKCU,
            stop_codes::LUAPRIV_NO_USERPROFILE,
            stop_codes::LUAPRIV_OK_OBJECT_PREFIX,
            stop_codes::LUAPRIV_RESTRICTED_NAMESPACE,
            stop_codes::LUAPRIV_NO_NAMESPACE,
            stop_codes::LUAPRIV_CANTGETPARENT,
            stop_codes::LUAPRIV_CANT_OPEN_PARENT,
            stop_codes::LUAPRIV_NON_LUA_USER,
            stop_codes::LUAPRIV_STRING2SID_FAILED,
            stop_codes::LUAPRIV_GETTOKENINFO,
            stop_codes::LUAPRIV_UNKNOWN_PRIVILEGE,
            stop_codes::LUAPRIV_PRIV_LOOKUP_FAILED,
            stop_codes::LUAPRIV_USED_PRIVILEGE_LUID,
            stop_codes::LUAPRIV_FAILED_PRIVILEGE_LUID,
            stop_codes::LUAPRIV_PRIVILEGED_USER,
            stop_codes::LUAPRIV_IRRELEVANT_PRIVILEGE_DENIED,
            stop_codes::LUAPRIV_IRRELEVANT_UNKNOWN_PRIVILEGE_DENIED,
            stop_codes::LUAPRIV_CANT_QUERY_VALUE,
            stop_codes::LUAPRIV_UNKNOWN_MAPPING,
            stop_codes::LUAPRIV_INI_PROFILE_ACCESS_DENIED,
            stop_codes::LUAPRIV_OK_OBJECT_DUMP,
            stop_codes::LUAPRIV_BAD_OBJECT_DUMP,
            stop_codes::LUAPRIV_SD2TEXT,
            stop_codes::LUAPRIV_DENY_ACE,
            stop_codes::LUAPRIV_RESTRICTED_RIGHT,
            stop_codes::LUAPRIV_RESTRICTED_RIGHT_MORE,
            stop_codes::LUAPRIV_CREATOR_OWNER,
            stop_codes::LUAPRIV_OK_OBJECT_GRANT,
            stop_codes::LUAPRIV_EMPTY_DACL,
            stop_codes::LUAPRIV_MISSING_PIECE,
            stop_codes::LUAPRIV_MISSING_ACE,
            stop_codes::LUAPRIV_MAXIMUM_ALLOWED,
            stop_codes::LUAPRIV_UNKNOWN_MAXIMUM_ALLOWED,
            stop_codes::LUAPRIV_UNKNOWN_PERMS,
            stop_codes::LUAPRIV_INI_PROFILE_ACCESS_GRANTED,
            stop_codes::LUAPRIV_CHECKTOKENMEMBERSHIP_TRUSTED,
            stop_codes::LUAPRIV_CHECKTOKENMEMBERSHIP_UNTRUSTED,
            stop_codes::LUAPRIV_INI_PROFILE_CONCERN,
            stop_codes::LUAPRIV_OP_REQUIRES_ACCESS,
            stop_codes::LUAPRIV_CANNOT_QUERY_ACCESS,
            stop_codes::LUAPRIV_ELEVATION_REQUIRED,
            stop_codes::LUAPRIV_ELEVATION_DETECTED,
            stop_codes::LUAPRIV_OBJECT_INACCESSIBLE,
            stop_codes::LUAPRIV_FAILED_API_CALL,
            stop_codes::LUAPRIV_SECURITY_LOG_OPENED,
            stop_codes::LUAPRIV_INI_PROFILE_FAILED,
            stop_codes::LUAPRIV_VIRTUALIZED_DELETION,
            stop_codes::LUAPRIV_UNKNOWN_API_OPTIONS,
            stop_codes::LUAPRIV_SET_GLOBAL_HOOK,
            stop_codes::LUAPRIV_SET_HOOK_FAILED,
            stop_codes::LUAPRIV_NETUSERGETINFO,
            stop_codes::LUAPRIV_SETACTIVEPWRSCHEME,
            stop_codes::LUAPRIV_SETACTIVEPWRSCHEME_FAILED,
            stop_codes::LUAPRIV_ACCESSCHECK,
            stop_codes::LUAPRIV_HARDADMINCHECK,
            stop_codes::LUAPRIV_FILE_NAME,
            stop_codes::LUAPRIV_FILE_VERSION,
            stop_codes::LUAPRIV_FILE_PRODUCT_VERSION,
            stop_codes::LUAPRIV_FILE_DESCRIPTION,
            stop_codes::LUAPRIV_FILE_PRODUCT_NAME,
            stop_codes::LUAPRIV_FILE_COMPANY_NAME,
            stop_codes::LUAPRIV_FILE_ORIGINAL_FILENAME,
            stop_codes::LUAPRIV_RESTRICTED_BY_MIC,
            stop_codes::LUAPRIV_LUAPRIV_VERSION,
        ],
        AppVerifierTest::PrintAPI => &[
            stop_codes::PRINTAPI_LEAKED_PRINTER_HANDLE,
            stop_codes::PRINTAPI_LEAKED_PRINTER_CHANGE_NOTIFICATION_HANDLE,
            stop_codes::PRINTAPI_LEAKED_PPRINTER_NOTIFY_INFO,
            stop_codes::PRINTAPI_MULTITHREADED_ACCESS_TO_PRINTER_HANDLE,
            stop_codes::PRINTAPI_PRINTER_HANDLE_ACCESSED_NOT_ON_THE_THREAD_THAT_OPENED_IT,
            stop_codes::PRINTAPI_PRINTER_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTAPI_INVALID_PRINTER_HANDLE,
            stop_codes::PRINTAPI_PRINTER_CHANGE_NOTIFICATION_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTAPI_UNKNOWN_PRINTER_CHANGE_NOTIFICATION_HANDLE,
            stop_codes::PRINTAPI_PRINTER_NOTIFY_INFO_ALREADY_FREED,
            stop_codes::PRINTAPI_INVALID_PRINTER_NOTIFY_INFO,
            stop_codes::PRINTAPI_TOO_MANY_OPENED_PRINTER_HANDLES,
            stop_codes::PRINTAPI_WINSPOOL_OPENPRINTER2W_EXPORTED_ON_PRE_VISTA_OS,
            stop_codes::PRINTAPI_TOO_MANY_OPENED_PRINT_TICKET_PROVIDER_HANDLES,
            stop_codes::PRINTAPI_PRINT_TICKET_PROVIDER_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTAPI_UNKNOWN_PRINT_TICKET_PROVIDER_HANDLE,
            stop_codes::PRINTAPI_MULTITHREADED_ACCESS_TO_PRINT_TICKET_PROVIDER_HANDLE,
            stop_codes::PRINTAPI_PRINT_TICKET_PROVIDER_HANDLE_ACCESSED_NOT_ON_THE_THREAD_THAT_OPENED_IT,
            stop_codes::PRINTAPI_LEAKED_PRINT_TICKET_PROVIDER_HANDLE,
            stop_codes::PRINTAPI_TOO_MANY_OPENED_PRINTER_CHANGE_NOTIFICATION_HANDLES,
            stop_codes::PRINTAPI_TOO_MANY_OPENED_PRINTER_NOTIFY_INFO_OBJECTS,
            stop_codes::PRINTAPI_INVALID_APPLICATION_PRINTTICKET,
            stop_codes::PRINTAPI_INVALID_APPLICATION_PRINTCAPABILITIES,
            stop_codes::PRINTAPI_PRINTTICKET_API_INVALID_NULL_ARGUMENT,
            stop_codes::PRINTAPI_PTCONFORM_UNEXPECTED_ERROR,
            stop_codes::PRINTAPI_UNSUPPORTED_API_CALL_IN_DLLMAIN,
            stop_codes::PRINTAPI_LEAKED_SPOOL_FILE_HANDLE,
            stop_codes::PRINTAPI_SPOOL_FILE_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTAPI_INVALID_SPOOL_FILE_HANDLE,
            stop_codes::PRINTAPI_TOO_MANY_OPENED_SPOOL_FILE_HANDLES,
            stop_codes::PRINTAPI_DEVMODE_BUFFER_SPANS_IN_NON_READABLE_MEMORY_PAGE,
            stop_codes::PRINTAPI_MODULE_UNLOAD,
            stop_codes::PRINTAPI_LEAKED_ASYNC_NOTIFY_HANDLE,
            stop_codes::PRINTAPI_INVALID_ASYNC_NOTIFY_HANDLE,
            stop_codes::PRINTAPI_ASYNC_NOTIFY_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTAPI_REFCOUNT_PLUS_AFTER_FAIL,
            stop_codes::PRINTAPI_REFCOUNT_PLUS_AFTER_API_FAIL,
            stop_codes::PRINTAPI_ASYNCCHANNEL_OS_CONTRACT_VIOLATION,
            stop_codes::PRINTAPI_ASYNCCHANNEL_CLIENT_CONTRACT_VIOLATION,
            stop_codes::PRINTAPI_ASYNCCHANNEL_CLOSECHANNEL_RACE_DETECTED,
            stop_codes::PRINTAPI_CALLING_NETBOUND_PRINT_API_ON_GUI_THREAD,
            stop_codes::PRINTAPI_UNSUPPORTED_API_CALLED_IN_SESSION_ZERO,
        ],
        AppVerifierTest::PrintDriver => &[
            stop_codes::PRINTDRIVER_FIRST_CHANCE_ACCESS_VIOLATION,
            stop_codes::PRINTDRIVER_INT_DIVIDE_BY_ZERO,
            stop_codes::PRINTDRIVER_DATATYPE_MISALIGNMENT,
            stop_codes::PRINTDRIVER_INVALID_HANDLE,
            stop_codes::PRINTDRIVER_PRINTER_HANDLE_ALREADY_CLOSED,
            stop_codes::PRINTDRIVER_INVALID_PRINTER_HANDLE,
            stop_codes::PRINTDRIVER_PLUGIN_CLOSED_PRINTER_HANDLE,
            stop_codes::PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_NUMBER_OF_SUPPORTED_SCHEMA_VERSIONS,
            stop_codes::PRINTDRIVER_PRINTTICKET_PROVIDER_MISSING_SUPPORTED_SCHEMA_VERSION,
            stop_codes::PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_SUPPORTED_SCHEMA_VERSION,
            stop_codes::PRINTDRIVER_PRINTTICKET_PROVIDER_INVALID_OEMPTOPTS,
            stop_codes::PRINTDRIVER_PRINTTICKET_PROVIDER_MISSING_NAMESPACE,
            stop_codes::PRINTDRIVER_PLUGIN_MISMATCHED_REFCOUNT,
            stop_codes::PRINTDRIVER_PPTL_IS_NULL_IN_OEMNEXTBAND,
            stop_codes::PRINTDRIVER_PLUGIN_PRIVATE_PDEV_IS_NULL,
            stop_codes::PRINTDRIVER_INVALID_PLUGIN_PRIVATE_DEVMODE_SIZE,
            stop_codes::PRINTDRIVER_PLUGIN_PRIVATE_DEVMODE_MISMATCHED_SIZE,
            stop_codes::PRINTDRIVER_INVALID_PLUGIN_SIGNATURE,
            stop_codes::PRINTDRIVER_PLUGIN_PRIVATE_DEVMODE_MISMATCHED_SIGNATURE,
            stop_codes::PRINTDRIVER_ENABLEDRIVER_FAILED,
            stop_codes::PRINTDRIVER_ENABLEDRIVER_FAILED_WITHOUT_ERROR_CODE,
            stop_codes::PRINTDRIVER_INVALID_SETBANDSIZE_CALL,
            stop_codes::PRINTDRIVER_INVALID_WRITEPRINTER_INITIALIZATION_CALL,
            stop_codes::PRINTDRIVER_WRITEPRINTER_FAILED,
            stop_codes::PRINTDRIVER_INVALID_COREDRIVER_PRINTTICKET,
            stop_codes::PRINTDRIVER_INVALID_PLUGIN_PRINTTICKET,
            stop_codes::PRINTDRIVER_INVALID_COREDRIVER_PRINTCAPABILITIES,
            stop_codes::PRINTDRIVER_INVALID_PLUGIN_PRINTCAPABILITIES,
            stop_codes::PRINTDRIVER_PTCONFORM_UNEXPECTED_ERROR,
            stop_codes::PRINTDRIVER_FILTER_INVALID_ARGUMENT,
            stop_codes::PRINTDRIVER_FILTER_PROPERTY_BAG_INVALID_CHANGE,
            stop_codes::PRINTDRIVER_FILTER_INVALID_CALL_ORDER,
            stop_codes::PRINTDRIVER_FILTER_REFCOUNT_MISMATCH,
            stop_codes::PRINTDRIVER_FILTER_UNEXPECTED_CALL,
            stop_codes::PRINTDRIVER_PIPELINE_INVALID_CALL_ORDER,
            stop_codes::PRINTDRIVER_PIPELINE_INVALID_INPUT_ARGUMENT,
            stop_codes::PRINTDRIVER_PIPELINE_INVALID_OUTPUT_ARGUMENT,
            stop_codes::PRINTDRIVER_SECURITY_CONTEXT_CHANGED_BY_A_PRINT_DRIVER_CALL,
            stop_codes::PRINTDRIVER_INVALID_FILTER_PRINTTICKET,
            stop_codes::PRINTDRIVER_INVALID_PIPELINE_PRINTTICKET,
            stop_codes::PRINTDRIVER_DLL_PREMATURE_UNLOAD,
            stop_codes::PRINTDRIVER_COM_INTERFACE_ALREADY_RELEASED,
            stop_codes::PRINTDRIVER_DRIVER_CALLED_EXITTHREAD,
            stop_codes::PRINTDRIVER_DRIVER_CALLED_TERMINATETHREAD,
            stop_codes::PRINTDRIVER_COM_APARTMENT_TYPE_CHANGED,
            stop_codes::PRINTDRIVER_COM_NOT_INITIALIZED,
            stop_codes::PRINTDRIVER_XML_DOM_REFCOUNT_CHANGED,
            stop_codes::PRINTDRIVER_FATALEXIT,
        ],
        AppVerifierTest::Networking => &[
            stop_codes::NETWORKING_UNSUPPORTED_API_CALL_IN_DLLMAIN,
            stop_codes::NETWORKING_WSA_SOCKET_ALREADY_CLOSED,
            stop_codes::NETWORKING_WSA_INVALID_SOCKET_HANDLE,
            stop_codes::NETWORKING_WSA_LEAKED_SOCKET_HANDLE,
            stop_codes::NETWORKING_WSP_SOCKET_ALREADY_CLOSED,
            stop_codes::NETWORKING_WSP_INVALID_SOCKET_HANDLE,
            stop_codes::NETWORKING_WSP_LEAKED_SOCKET_HANDLE,
            stop_codes::NETWORKING_WSA_NOT_INITIALIZED,
            stop_codes::NETWORKING_WSP_NOT_INITIALIZED,
            stop_codes::NETWORKING_NSP_NOT_INITIALIZED,
            stop_codes::NETWORKING_INVALID_FUNCTION_POINTER_DETECTED,
            stop_codes::NETWORKING_WSA_SOCKETS_ABORTED,
            stop_codes::NETWORKING_WSP_SOCKETS_ABORTED,
            stop_codes::NETWORKING_WSA_RETURN_INVALID,
            stop_codes::NETWORKING_WSP_RETURN_INVALID,
        ],
        AppVerifierTest::NTLMCaller => &[
            stop_codes::NTLMCALLER_ACH_EXPLICIT_NTLM_PACKAGE,
            stop_codes::NTLMCALLER_ACH_IMPLICITLY_USE_NTLM,
            stop_codes::NTLMCALLER_ACH_BAD_NTLM_EXCLUSION,
            stop_codes::NTLMCALLER_ISC_MALFORMED_TARGET,
        ],
        AppVerifierTest::NTLMDowngrade => &[
            stop_codes::NTLMDOWNGRADE_FALLBACK_TO_NTLM,
        ],
        AppVerifierTest::Webservices => &[
            stop_codes::WEBSERVICES_INVALID_OBJECT_ADDRESS,
            stop_codes::WEBSERVICES_SINGLE_THREADED_OBJECT_VIOLATION,
            stop_codes::WEBSERVICES_OBJECT_IN_USE,
            stop_codes::WEBSERVICES_API_TIMEOUT,
            stop_codes::WEBSERVICES_CORRUPT_CALL_CONTEXT,
        ],
        AppVerifierTest::Cuzz => &[
            stop_codes::CUZZ_DATA_RACE,
        ],
    }
}

struct ArgsBuilder {
    args: Vec<OsString>,
}

impl ArgsBuilder {
    pub fn new() -> Self {
        ArgsBuilder { args: Vec::new() }
    }

    pub fn from_args<I: IntoIterator<Item = S>, S: Into<OsString>>(args: I) -> Self {
        let mut builder = ArgsBuilder::new();
        builder.args(args);
        builder
    }

    pub fn arg<S: Into<OsString>>(&mut self, arg: S) -> &mut Self {
        self.args.push(arg.into());
        self
    }

    pub fn args<I: IntoIterator<Item = S>, S: Into<OsString>>(&mut self, args: I) -> &mut Self {
        for arg in args.into_iter() {
            self.arg(arg);
        }
        self
    }

    pub fn get(self) -> Vec<OsString> {
        self.args
    }
}

#[derive(Clone)]
pub struct ArgsWithComments {
    pub args: Vec<OsString>,
    pub comments: &'static [&'static str],
}

impl ArgsWithComments {
    pub fn new(args: Vec<OsString>, comments: &'static [&'static str]) -> Self {
        ArgsWithComments { args, comments }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum AppVerifierState {
    Enabled,
    Disabled,
}

#[derive(Clone)]
pub struct AppVerifierController {
    appverif_path: PathBuf,
    state: Arc<Mutex<AppVerifierState>>,
    enable_command_lines: [ArgsWithComments; 3],
    disable_command_lines: [ArgsWithComments; 2],
}

impl AppVerifierController {
    pub fn new(exe_name: &OsStr, app_verifier_tests: &[String]) -> Result<Self> {
        let mut enable_args = ArgsBuilder::new();
        let mut configure_args = ArgsBuilder::new();

        enable_args.arg("-enable");
        configure_args.arg("-configure");
        for test in app_verifier_tests.iter() {
            enable_args.arg(format!("{}", test));

            for stop_code in stop_codes(AppVerifierTest::from_str(&*test)?) {
                configure_args.arg(format!("0x{:x}", *stop_code));
            }
        }

        // I believe this turns all stops into exceptions.
        // We also do that explicitly for each stop, this might help if we missed a stop code.
        enable_args
            .arg("-for")
            .arg(exe_name)
            .arg("-with")
            .arg("core.ExceptionOnStop=true");

        // TODO: consider setting properties on tests, e.g.
        //   Heaps - Dlls (only use pageheap for specific dlls)
        //           Random (we might miss some, repro can't use random)

        // Possible options
        // 0x001 - stop is active
        // 0x020 - stop will break into debugger using a breakpoint
        // 0x040 - stop will break into debugger by generating a verifier exception
        // 0x080 - stop will be logged in the log file
        // 0x100 - stack trace for this stop will be logged in the log file
        //
        // We only want the exception.
        configure_args
            .arg("-for")
            .arg(exe_name)
            .arg("-with")
            .arg("ErrorReport=0x41");

        let disable_logfile_args = ArgsBuilder::from_args(&["-logtofile", "disable"]);

        let mut disable_args = ArgsBuilder::from_args(&["-disable", "*", "-for"]);
        disable_args.arg(exe_name);

        // We disable appverifier file logging while fuzzing.
        //
        // The command line tool does not provide a way to query the current setting, so we restore
        // to the default setting of `enable`.
        //
        // We could query the registry directly:
        //
        //   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{ApplicationVerifierGlobalSettings}\LogToFile
        //
        // but this setting is not officially documented. We take the conservative approach and assume
        // the user never changes this setting.
        let enable_logfile_args = ArgsBuilder::from_args(&["-logtofile", "enable"]);

        let appverif_path = {
            let mut buf =
                PathBuf::from(env::var("SystemRoot").unwrap_or_else(|_| "C:\\WINDOWS".into()));
            buf.push("system32");
            buf.push("appverif.exe");
            buf
        };

        Ok(AppVerifierController {
            appverif_path,
            state: Arc::new(Mutex::new(AppVerifierState::Disabled)),

            enable_command_lines: [
                ArgsWithComments::new(enable_args.get(),
                                      &["Enable configured checks to stop with an exception for the program under test."]),
                ArgsWithComments::new(configure_args.get(),
                                      &["Configure the stop codes for the configured checks as follows:",
                                        "",
                                        "0x001 - stop is active",
                                        "0x020 - stop will break into debugger using a breakpoint",
                                        "0x040 - stop will break into debugger by generating a verifier exception",
                                        "0x080 - stop will be logged in the log file",
                                        "0x100 - stack trace for this stop will be logged in the log file",
                                        "",
                                        "We want no file logging and no breakpoints, but we do want notification.",
                                      ]),
                ArgsWithComments::new(disable_logfile_args.get(),
                                      &["Disable all file logging (a file gets created even if there are no errors)"]),
            ],

            disable_command_lines: [
                ArgsWithComments::new(disable_args.get(),
                                      &["Disable all checks for the program under test."]),
                ArgsWithComments::new(enable_logfile_args.get(),
                                      &["Reenable previously disabled file logging."]),
            ],
        })
    }

    pub fn appverif_path(&self) -> &Path {
        &self.appverif_path
    }

    pub fn set(&self, new_state: AppVerifierState) -> Result<()> {
        let mut state = self.state.lock().unwrap();
        if new_state != *state {
            let command_lines = match new_state {
                AppVerifierState::Enabled => self.enable_command_lines(),
                AppVerifierState::Disabled => self.disable_command_lines(),
            };

            for args in command_lines.iter() {
                self.run(&args.args)?;
            }

            *state = new_state;
        }
        Ok(())
    }

    pub fn enable_command_lines(&self) -> &[ArgsWithComments] {
        &self.enable_command_lines
    }

    pub fn disable_command_lines(&self) -> &[ArgsWithComments] {
        &self.disable_command_lines
    }

    fn run(&self, args: &[OsString]) -> Result<()> {
        anyhow::ensure!(
            process::is_elevated(),
            "running appverifier requires running elevated"
        );

        debug!(
            "Running appverif: {}",
            logging::command_invocation(&self.appverif_path, args)
        );
        let child = Command::new(&self.appverif_path)
            .args(args)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .context("running appverif")?;
        let pid = child.id();
        let output = child.wait_with_output()?;
        debug!("appverif: {:?}", logging::ProcessDetails::new(pid, &output));

        Ok(())
    }
}
