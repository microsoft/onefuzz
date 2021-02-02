# OneFuzz Telemetry Tool

CLI that parses a local copy of [exported telemetry](https://docs.microsoft.com/en-us/azure/azure-monitor/app/export-telemetry#setup/).  Use [azcopy](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-v10) to copy the telemetry locally.

## Usage

```bash
mkdir -p stats
azcopy sync 'INSTANCE_SAS_URL' stats
find stats -type f | onefuzz-telemetry-stats
```
