use anyhow::Result;

use super::parse_available_kb;

#[test]
fn test_parse_available_kb() -> Result<()> {
    assert_eq!(parse_available_kb(MEMINFO)?, 1001092);
    assert_eq!(parse_available_kb("MemAvailable:    1001092 kB")?, 1001092);
    assert_eq!(
        parse_available_kb("MemAvailable:    1001092 kB\tMemAvailable:    123 kB")?,
        1001092
    );
    assert_eq!(
        parse_available_kb("    MemAvailable:      1001092 kB")?,
        1001092
    );
    assert_eq!(parse_available_kb("    MemAvailable:1001092 kB")?, 1001092);
    assert_eq!(parse_available_kb("    MemAvailable: 1001092 kB")?, 1001092);
    assert_eq!(
        parse_available_kb("    MemAvailable:      1001092 kB")?,
        1001092
    );
    assert_eq!(
        parse_available_kb("extra    MemAvailable:      1001092 kB")?,
        1001092
    );
    assert_eq!(
        parse_available_kb("extra    MemAvailable:1001092 kB")?,
        1001092
    );
    assert_eq!(
        parse_available_kb("extra    MemAvailable: 1001092 kB")?,
        1001092
    );
    assert_eq!(
        parse_available_kb("extra    MemAvailable:      1001092 kB")?,
        1001092
    );

    Ok(())
}

#[test]
fn test_parse_available_kb_missing() {
    assert!(parse_available_kb("").is_err());
    assert!(parse_available_kb("1001092").is_err());
    assert!(parse_available_kb("MemAvailable: ").is_err());
    assert!(parse_available_kb("MemAvailable: 1001092 MB").is_err());
    assert!(parse_available_kb("MemFree: 198308 kB").is_err());
}

const MEMINFO: &str = "MemTotal:       16036984 kB
MemFree:          198308 kB
MemAvailable:    1001092 kB
Buffers:          521880 kB
Cached:           459416 kB
SwapCached:         1580 kB
Active:           830140 kB
Inactive:         206728 kB
Active(anon):      22492 kB
Inactive(anon):    28876 kB
Active(file):     807648 kB
Inactive(file):   177852 kB
Unevictable:           0 kB
Mlocked:               0 kB
SwapTotal:       4194300 kB
SwapFree:        4181440 kB
Dirty:                 8 kB
Writeback:             0 kB
AnonPages:         54368 kB
Mapped:            31344 kB
Shmem:               792 kB
Slab:             192900 kB
SReclaimable:     131056 kB
SUnreclaim:        61844 kB
KernelStack:        3104 kB
PageTables:         5324 kB
NFS_Unstable:          0 kB
Bounce:                0 kB
WritebackTmp:          0 kB
CommitLimit:    12212792 kB
Committed_AS:     575108 kB
VmallocTotal:   34359738367 kB
VmallocUsed:           0 kB
VmallocChunk:          0 kB
HardwareCorrupted:     0 kB
AnonHugePages:         0 kB
ShmemHugePages:        0 kB
ShmemPmdMapped:        0 kB
CmaTotal:              0 kB
CmaFree:               0 kB
HugePages_Total:       0
HugePages_Free:        0
HugePages_Rsvd:        0
HugePages_Surp:        0
Hugepagesize:       2048 kB
DirectMap4k:      152880 kB
DirectMap2M:     4696064 kB
DirectMap1G:    11534336 kB";
