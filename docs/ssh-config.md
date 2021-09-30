# SSH within OneFuzz

OneFuzz enables automatically connecting to fuzzing & crash repro nodes via SSH.
Each VM and VM scale set has its own SSH key pair.

On Linux VMs, the public key is written to `~onefuzz/.ssh/authorized_keys`

For Windows VMs, the public key is written to
`\$env:ProgramData\ssh\administrators_authorized_keys` following
[Windows OpenSSH server guides](https://docs.microsoft.com/en-us/windows-server/administration/openssh/openssh_server_configuration).

## OneFuzz cli handling keys

When using any of the SSH enabled components of the onefuzz CLI, the CLI will
automatically fetch the key-pair for a VM as needed. The private key is written
to a temporary directory and removed upon completion of the SSH command.

NOTE: As VMs and VM scale sets are intended to be short-lived and ephemeral, the
onefuzz CLI configures SSH to not write to the user's known host file and
ignores host key checking.
