// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#![allow(clippy::new_without_default)]

/// This module provides a Rust friendly wrapper around a Win32 JobObject.
use std::{
    convert::TryFrom, mem::MaybeUninit, os::windows::io::AsRawHandle, process::Child, ptr,
    time::Duration,
};

use anyhow::Result;
use winapi::{
    shared::minwindef::{FALSE, LPVOID, TRUE},
    um::{
        handleapi::{CloseHandle, INVALID_HANDLE_VALUE},
        ioapiset::{CreateIoCompletionPort, GetQueuedCompletionStatus},
        jobapi2::{
            AssignProcessToJobObject, CreateJobObjectW, QueryInformationJobObject,
            SetInformationJobObject,
        },
        minwinbase::LPSECURITY_ATTRIBUTES,
        processthreadsapi::GetCurrentProcess,
        winbase::INFINITE,
        winnt::{
            JobObjectAssociateCompletionPortInformation, JobObjectBasicAndIoAccountingInformation,
            JobObjectExtendedLimitInformation, JobObjectNotificationLimitInformation, HANDLE,
            IO_COUNTERS, JOBOBJECT_ASSOCIATE_COMPLETION_PORT,
            JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION, JOBOBJECT_BASIC_LIMIT_INFORMATION,
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION,
            JOB_OBJECT_LIMIT_ACTIVE_PROCESS, JOB_OBJECT_LIMIT_AFFINITY,
            JOB_OBJECT_LIMIT_BREAKAWAY_OK, JOB_OBJECT_LIMIT_JOB_MEMORY,
            JOB_OBJECT_LIMIT_JOB_READ_BYTES, JOB_OBJECT_LIMIT_JOB_TIME,
            JOB_OBJECT_LIMIT_JOB_WRITE_BYTES, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            JOB_OBJECT_LIMIT_PRIORITY_CLASS, JOB_OBJECT_LIMIT_PROCESS_MEMORY,
            JOB_OBJECT_LIMIT_PROCESS_TIME, JOB_OBJECT_LIMIT_SCHEDULING_CLASS,
            JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK, JOB_OBJECT_LIMIT_WORKINGSET,
            JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS, JOB_OBJECT_MSG_ACTIVE_PROCESS_LIMIT,
            JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO, JOB_OBJECT_MSG_END_OF_JOB_TIME,
            JOB_OBJECT_MSG_END_OF_PROCESS_TIME, JOB_OBJECT_MSG_EXIT_PROCESS,
            JOB_OBJECT_MSG_JOB_MEMORY_LIMIT, JOB_OBJECT_MSG_NEW_PROCESS,
            JOB_OBJECT_MSG_NOTIFICATION_LIMIT, JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT, LARGE_INTEGER,
        },
    },
};

use crate::{
    handle::Handle,
    {last_os_error, string},
};

#[derive(Clone)]
pub struct JobObject {
    handle: Handle,
    name: String,
}

impl JobObject {
    pub fn new(name: &str) -> Result<JobObject> {
        let handle = unsafe {
            CreateJobObjectW(
                ptr::null_mut() as LPSECURITY_ATTRIBUTES,
                string::to_wstring(&name).as_ptr(),
            )
        };

        if handle == ptr::null_mut() as HANDLE {
            return Err(std::io::Error::last_os_error().into());
        }

        let handle = Handle(handle);
        Ok(JobObject {
            handle,
            name: name.into(),
        })
    }

    fn attach_process(&self, handle: HANDLE) -> Result<()> {
        let res = unsafe { AssignProcessToJobObject(self.handle.0, handle) };
        if res == FALSE {
            Err(std::io::Error::last_os_error().into())
        } else {
            Ok(())
        }
    }

    pub fn attach_current_process(&self) -> Result<()> {
        self.attach_process(unsafe { GetCurrentProcess() })
    }

    pub fn attach(&self, child: &Child) -> Result<()> {
        self.attach_process(child.as_raw_handle())
    }

    pub fn release(&mut self) -> Result<()> {
        let handle = self.handle.0;
        self.handle.0 = INVALID_HANDLE_VALUE;
        let res = unsafe { CloseHandle(handle) };
        if res == FALSE {
            Err(std::io::Error::last_os_error().into())
        } else {
            Ok(())
        }
    }

    pub fn set_information(&self, info: &mut JobInformation) -> Result<()> {
        if let Some(mut extended_limit_information) = info.extended_limit_information {
            let res = unsafe {
                SetInformationJobObject(
                    self.handle.0,
                    JobObjectExtendedLimitInformation,
                    &mut extended_limit_information as *mut JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                        as LPVOID,
                    std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
                )
            };

            if res == FALSE {
                return Err(last_os_error());
            }
        }

        if let Some(mut notification_information) = info.notification_information {
            let res = unsafe {
                SetInformationJobObject(
                    self.handle.0,
                    JobObjectNotificationLimitInformation,
                    &mut notification_information as *mut JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION
                        as LPVOID,
                    std::mem::size_of::<JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION>() as u32,
                )
            };

            if res == FALSE {
                return Err(last_os_error());
            }
        }

        Ok(())
    }

    pub fn set_notification_routine<F: Fn(JobObjectNotification) + Send + 'static>(
        &mut self,
        f: F,
    ) -> Result<()> {
        const NUM_CONCURRENT_THREADS: u32 = 0;
        const COMPLETION_KEY: LPVOID = ptr::null_mut();
        let completion_port = unsafe {
            CreateIoCompletionPort(
                INVALID_HANDLE_VALUE,
                ptr::null_mut(),
                COMPLETION_KEY as usize,
                NUM_CONCURRENT_THREADS,
            )
        };

        let completion_port = Handle(completion_port);

        let completion_port_clone = completion_port.clone();
        let job_handle_clone = Handle(self.handle.0);

        // We specify a stack size we hope is sufficient to not require any additional allocation
        // when we're running out of memory to give us a chance to at least report being out of
        // memory. The size is based on similar code in EDGE.
        std::thread::Builder::new()
            .stack_size(32 * 1024)
            .name(format!("{} notification routine", self.name))
            .spawn(move || {
                jobobject_notification_threadproc(completion_port_clone, job_handle_clone, f)
            })?;

        let mut port_info = JOBOBJECT_ASSOCIATE_COMPLETION_PORT {
            CompletionKey: COMPLETION_KEY,
            CompletionPort: completion_port.0,
        };

        let res = unsafe {
            SetInformationJobObject(
                self.handle.0,
                JobObjectAssociateCompletionPortInformation,
                &mut port_info as *mut JOBOBJECT_ASSOCIATE_COMPLETION_PORT as LPVOID,
                std::mem::size_of::<JOBOBJECT_ASSOCIATE_COMPLETION_PORT>() as u32,
            )
        };

        if res == FALSE {
            return Err(last_os_error());
        }

        Ok(())
    }

    pub fn query_job_stats(&self) -> Result<JobStats> {
        let mut accounting_info =
            MaybeUninit::<JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION>::zeroed();

        let res = unsafe {
            QueryInformationJobObject(
                self.handle.0,
                JobObjectBasicAndIoAccountingInformation,
                accounting_info.as_mut_ptr() as LPVOID,
                std::mem::size_of::<JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION>() as u32,
                ptr::null_mut(),
            )
        };

        if res == FALSE {
            return Err(last_os_error());
        }

        let mut limit_info = MaybeUninit::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>::zeroed();

        let res = unsafe {
            QueryInformationJobObject(
                self.handle.0,
                JobObjectExtendedLimitInformation,
                limit_info.as_mut_ptr() as LPVOID,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
                ptr::null_mut(),
            )
        };

        if res == FALSE {
            return Err(last_os_error());
        }

        let job_stats =
            (unsafe { (accounting_info.assume_init(), limit_info.assume_init()) }).into();

        Ok(job_stats)
    }
}

#[derive(Debug, Default)]
pub struct JobStats {
    pub total_user_time: Duration,
    pub total_kernel_time: Duration,
    pub total_page_fault_count: u32,
    pub total_processes: u32,
    pub active_processes: u32,
    pub total_terminated_processes: u32,
    pub peak_job_memory_used: usize,
    pub peak_process_memory_used: usize,
}

impl
    From<(
        JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
    )> for JobStats
{
    fn from(
        (accounting_info, limit_info): (
            JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION,
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
        ),
    ) -> Self {
        let basic_info = &accounting_info.BasicInfo;
        Self {
            total_kernel_time: duration_from_100ns_ticks(basic_info.TotalKernelTime),
            total_user_time: duration_from_100ns_ticks(basic_info.TotalUserTime),
            total_page_fault_count: basic_info.TotalPageFaultCount,
            total_processes: basic_info.TotalProcesses,
            active_processes: basic_info.ActiveProcesses,
            total_terminated_processes: basic_info.TotalTerminatedProcesses,
            peak_job_memory_used: limit_info.PeakJobMemoryUsed,
            peak_process_memory_used: limit_info.PeakProcessMemoryUsed,
        }
    }
}

pub struct JobInformation {
    extended_limit_information: Option<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>,
    notification_information: Option<JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION>,
}

impl JobInformation {
    pub fn new() -> Self {
        Self {
            extended_limit_information: None,
            notification_information: None,
        }
    }

    fn extended_limit_information_ref(&mut self) -> &mut JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        self.extended_limit_information.get_or_insert_with(|| {
            JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
                BasicLimitInformation: JOBOBJECT_BASIC_LIMIT_INFORMATION {
                    PerProcessUserTimeLimit: large_integer(0),
                    PerJobUserTimeLimit: large_integer(0),
                    LimitFlags: 0,
                    MinimumWorkingSetSize: 0,
                    MaximumWorkingSetSize: 0,
                    ActiveProcessLimit: 0,
                    Affinity: 0,
                    PriorityClass: 0,
                    SchedulingClass: 0,
                },
                IoInfo: IO_COUNTERS {
                    ReadOperationCount: 0,
                    WriteOperationCount: 0,
                    OtherOperationCount: 0,
                    ReadTransferCount: 0,
                    WriteTransferCount: 0,
                    OtherTransferCount: 0,
                },
                ProcessMemoryLimit: 0,
                JobMemoryLimit: 0,
                PeakProcessMemoryUsed: 0,
                PeakJobMemoryUsed: 0,
            }
        })
    }

    pub fn per_process_user_time_limit(&mut self, limit: Duration) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_PROCESS_TIME;
        extended_info.BasicLimitInformation.PerProcessUserTimeLimit =
            large_integer(duration_as_100ns_ticks(&limit));
        self
    }

    pub fn per_job_user_time_limit(&mut self, limit: Duration) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_JOB_TIME;
        extended_info.BasicLimitInformation.PerJobUserTimeLimit =
            large_integer(duration_as_100ns_ticks(&limit));
        self
    }

    pub fn minimum_working_set_size(&mut self, limit: usize) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_WORKINGSET;
        extended_info.BasicLimitInformation.MinimumWorkingSetSize = limit;
        self
    }

    pub fn maximum_working_set_size(&mut self, limit: usize) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_WORKINGSET;
        extended_info.BasicLimitInformation.MaximumWorkingSetSize = limit;
        self
    }

    pub fn active_process_limit(&mut self, limit: u32) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        extended_info.BasicLimitInformation.ActiveProcessLimit = limit;
        self
    }

    pub fn affinity(&mut self, limit: usize) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_AFFINITY;
        extended_info.BasicLimitInformation.Affinity = limit;
        self
    }

    pub fn priority_class(&mut self, limit: u32) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_PRIORITY_CLASS;
        extended_info.BasicLimitInformation.PriorityClass = limit;
        self
    }

    pub fn scheduling_class(&mut self, limit: u32) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_SCHEDULING_CLASS;
        extended_info.BasicLimitInformation.SchedulingClass = limit;
        self
    }

    pub fn process_memory_limit(&mut self, limit: usize) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        extended_info.ProcessMemoryLimit = limit;
        self
    }

    pub fn job_memory_limit(&mut self, limit: usize) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_JOB_MEMORY;
        extended_info.JobMemoryLimit = limit;
        self
    }

    pub fn breakaway_ok(&mut self) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_BREAKAWAY_OK;
        self
    }

    pub fn silent_breakaway_ok(&mut self) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;
        self
    }

    pub fn kill_on_job_close(&mut self) -> &mut Self {
        let extended_info = self.extended_limit_information_ref();
        extended_info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        self
    }

    fn notify_info_ref(&mut self) -> &mut JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION {
        self.notification_information.get_or_insert_with(|| {
            JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION {
                IoReadBytesLimit: 0,
                IoWriteBytesLimit: 0,
                PerJobUserTimeLimit: large_integer(0),
                JobMemoryLimit: 0,
                RateControlTolerance: 0,
                RateControlToleranceInterval: 0,
                LimitFlags: 0,
            }
        })
    }

    pub fn notify_io_read_bytes_limit(&mut self, limit: u64) -> &mut Self {
        let notify_info = self.notify_info_ref();
        notify_info.IoReadBytesLimit = limit;
        notify_info.LimitFlags |= JOB_OBJECT_LIMIT_JOB_READ_BYTES;
        self
    }

    pub fn notify_io_write_bytes_limit(&mut self, limit: u64) -> &mut Self {
        let notify_info = self.notify_info_ref();
        notify_info.IoWriteBytesLimit = limit;
        notify_info.LimitFlags |= JOB_OBJECT_LIMIT_JOB_WRITE_BYTES;
        self
    }

    pub fn notify_per_job_user_time_limit(&mut self, limit: Duration) -> &mut Self {
        let notify_info = self.notify_info_ref();
        notify_info.PerJobUserTimeLimit = large_integer(duration_as_100ns_ticks(&limit));
        notify_info.LimitFlags |= JOB_OBJECT_LIMIT_JOB_TIME;
        self
    }

    pub fn notify_job_memory_limit(&mut self, limit: u64) -> &mut Self {
        let notify_info = self.notify_info_ref();
        notify_info.JobMemoryLimit = limit;
        notify_info.LimitFlags |= JOB_OBJECT_LIMIT_JOB_MEMORY;
        self
    }
}

#[derive(Debug)]
#[allow(dead_code)]
pub struct JobObjectLimitNotification {
    io_read_bytes_limit: Option<u64>,
    io_write_bytes_limit: Option<u64>,
    per_job_user_time_limit: Option<Duration>,
    job_memory_limit: Option<u64>,
}

impl From<JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION> for JobObjectLimitNotification {
    fn from(info: JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION) -> Self {
        let io_read_bytes_limit = if info.LimitFlags & JOB_OBJECT_LIMIT_JOB_READ_BYTES != 0 {
            Some(info.IoReadBytesLimit)
        } else {
            None
        };
        let io_write_bytes_limit = if info.LimitFlags & JOB_OBJECT_LIMIT_JOB_WRITE_BYTES != 0 {
            Some(info.IoWriteBytesLimit)
        } else {
            None
        };
        let per_job_user_time_limit = if info.LimitFlags & JOB_OBJECT_LIMIT_JOB_TIME != 0 {
            Some(duration_from_100ns_ticks(info.PerJobUserTimeLimit))
        } else {
            None
        };
        let job_memory_limit = if info.LimitFlags & JOB_OBJECT_LIMIT_JOB_MEMORY != 0 {
            Some(info.JobMemoryLimit)
        } else {
            None
        };

        JobObjectLimitNotification {
            io_read_bytes_limit,
            io_write_bytes_limit,
            per_job_user_time_limit,
            job_memory_limit,
        }
    }
}

#[derive(Debug)]
pub enum JobObjectNotification {
    AbnormalExitProcess(u32),
    ActiveProcessLimit,
    ActiveProcessZero,
    EndOfJobTime,
    EndOfProcessTime(u32),
    ExitProcess(u32),
    JobMemoryLimit(u32),
    NewProcess(u32),
    NotificationLimit(JobObjectLimitNotification),
    ProcessMemoryLimit(u32),
    UnknownMessage(u32),
}

impl JobObjectNotification {
    pub fn new(msg: u32, pid: u32, job_handle: HANDLE) -> Self {
        match msg {
            JOB_OBJECT_MSG_END_OF_JOB_TIME => JobObjectNotification::EndOfJobTime,
            JOB_OBJECT_MSG_END_OF_PROCESS_TIME => JobObjectNotification::EndOfProcessTime(pid),
            JOB_OBJECT_MSG_ACTIVE_PROCESS_LIMIT => JobObjectNotification::ActiveProcessLimit,
            JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO => JobObjectNotification::ActiveProcessZero,
            JOB_OBJECT_MSG_NEW_PROCESS => JobObjectNotification::NewProcess(pid),
            JOB_OBJECT_MSG_EXIT_PROCESS => JobObjectNotification::ExitProcess(pid),
            JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS => JobObjectNotification::AbnormalExitProcess(pid),
            JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT => JobObjectNotification::ProcessMemoryLimit(pid),
            JOB_OBJECT_MSG_JOB_MEMORY_LIMIT => JobObjectNotification::JobMemoryLimit(pid),
            JOB_OBJECT_MSG_NOTIFICATION_LIMIT => {
                let mut notification_limit_info =
                    MaybeUninit::<JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION>::zeroed();
                let res = unsafe {
                    QueryInformationJobObject(
                        job_handle,
                        JobObjectNotificationLimitInformation,
                        notification_limit_info.as_mut_ptr() as LPVOID,
                        std::mem::size_of::<JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION>() as u32,
                        ptr::null_mut(),
                    )
                };

                if res == FALSE {
                    JobObjectNotification::UnknownMessage(msg)
                } else {
                    let notification_limit_info = unsafe { notification_limit_info.assume_init() };
                    JobObjectNotification::NotificationLimit(notification_limit_info.into())
                }
            }
            _ => JobObjectNotification::UnknownMessage(msg),
        }
    }
}

fn jobobject_notification_threadproc<F: Fn(JobObjectNotification)>(
    completion_port: Handle,
    job_handle: Handle,
    f: F,
) -> ! {
    loop {
        let mut message_id = MaybeUninit::zeroed();
        let mut completion_key = MaybeUninit::zeroed();
        let mut process_id = MaybeUninit::zeroed();
        let res = unsafe {
            GetQueuedCompletionStatus(
                completion_port.0,
                message_id.as_mut_ptr(),
                completion_key.as_mut_ptr(),
                process_id.as_mut_ptr(),
                INFINITE,
            )
        };
        if res == TRUE {
            let msg = JobObjectNotification::new(
                unsafe { message_id.assume_init() },
                unsafe { process_id.assume_init() } as u32,
                job_handle.0,
            );
            f(msg);
        }
    }
}

fn duration_from_100ns_ticks(duration: LARGE_INTEGER) -> Duration {
    let duration = unsafe { *duration.QuadPart() } as u64;
    Duration::from_nanos(duration.saturating_mul(100))
}

/// Return the duration as a count of 100ns ticks.
/// Defaults to i64::MAX if the duration is too long which is sufficient for job object purposes.
fn duration_as_100ns_ticks(duration: &Duration) -> i64 {
    duration
        .as_nanos()
        .checked_div(100)
        .and_then(|v: u128| i64::try_from(v).ok())
        .unwrap_or(std::i64::MAX)
}

fn large_integer(val: i64) -> LARGE_INTEGER {
    let mut result: LARGE_INTEGER;
    unsafe {
        result = std::mem::zeroed();
        *result.QuadPart_mut() = val;
    };
    result
}

#[cfg(test)]
mod tests {
    use std::process::{Command, Stdio};

    use super::*;

    #[allow(unused)]
    fn debug_job_notifications(job_name: &str, msg: &JobObjectNotification) {
        // Uncomment to debug job notifications.
        // If left active, it can mess up normal test output.
        //println!("job({}) msg: {:?}", job_name, msg);
    }

    fn create_job_common<F>(name: &'static str, f: F) -> JobObject
    where
        F: Fn(&mut JobInformation) -> &mut JobInformation,
    {
        let job = JobObject::new(name).expect("Job creation failed");

        let mut job_info = JobInformation::new();
        f(&mut job_info);
        job.set_information(&mut job_info)
            .expect("Setting job information failed");

        job
    }

    fn create_job_with_notification<F, G>(name: &'static str, f: F, notify_callback: G) -> JobObject
    where
        F: Fn(&mut JobInformation) -> &mut JobInformation,
        G: Fn(JobObjectNotification) + Send + 'static,
    {
        let mut job = create_job_common(name, f);

        job.set_notification_routine(move |msg| {
            debug_job_notifications(name, &msg);
            notify_callback(msg);
        })
        .expect("setting notification routine");

        job
    }

    fn create_job<F>(name: &'static str, f: F) -> JobObject
    where
        F: Fn(&mut JobInformation) -> &mut JobInformation,
    {
        let mut job = create_job_common(name, f);

        job.set_notification_routine(move |msg| {
            debug_job_notifications(name, &msg);
        })
        .expect("setting notification routine");

        job
    }

    #[test]
    fn limit_job_user_time() {
        let job = create_job("limit_job_user_time", |builder| {
            builder.per_job_user_time_limit(Duration::from_millis(100))
        });

        let child = Command::new("cmd.exe")
            .stdout(Stdio::piped())
            .args(&["/c", "@echo off && for /L %x in (0,1,100000000) DO echo %x"])
            .spawn()
            .expect("launching child process for job test failed");

        job.attach(&child).expect("attaching process to job failed");
        let output = child.wait_with_output().unwrap();

        assert!(!output.status.success());
    }

    #[test]
    fn limit_process_user_time() {
        let job = create_job("limit_process_user_time", |builder| {
            builder.per_process_user_time_limit(Duration::from_millis(100))
        });

        let child = Command::new("cmd.exe")
            .stdout(Stdio::piped())
            .args(&["/c", "@echo off && for /L %x in (0,1,100000000) DO echo %x"])
            .spawn()
            .expect("launching child process for job test failed");

        job.attach(&child).expect("attaching process to job failed");
        let output = child.wait_with_output().unwrap();

        assert!(!output.status.success());
    }

    #[test]
    fn job_memory_limit() {
        let job = create_job("job_memory_limit", |builder| {
            builder.job_memory_limit(10 * 1024 * 1024)
        });

        // Attaching should terminate PowerShell as it requires much more than 10MB, but
        // we create a very large array of integers to be extra sure we'll exceed 10MB.
        let child = Command::new("powershell.exe")
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .args(&["/c", "$x=1..10mb"])
            .spawn()
            .expect("launching child process for job test failed");

        job.attach(&child).expect("attaching process to job failed");
        let output = child.wait_with_output().unwrap();
        assert!(!output.status.success());
    }

    #[test]
    fn job_notification_limit_process_time() {
        let limit = Duration::from_millis(100);
        let (tx, rx) = std::sync::mpsc::channel();
        let job = create_job_with_notification(
            "notification_limit_process_time",
            |builder| builder.notify_per_job_user_time_limit(limit),
            move |msg| {
                if let JobObjectNotification::NotificationLimit(info) = msg {
                    tx.send(info).ok();
                }
            },
        );

        let child = Command::new("powershell.exe")
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .args(&["/c", "$x=1..10000|%{$_}|%{$_}"])
            .spawn()
            .expect("launching child process for job test failed");

        job.attach(&child).expect("attaching process to job failed");
        let output = child.wait_with_output().unwrap();
        assert!(output.status.success());

        let info = rx.recv().expect("should have received notification");
        assert!(info.per_job_user_time_limit.is_some());
        assert_eq!(info.per_job_user_time_limit.unwrap(), limit);
    }
}