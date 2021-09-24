# Containers in OneFuzz

An
[Azure Blob Storage Container](https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blobs-introduction).
Each fuzzing task has a set of required and potentially optional containers that
are used in a specific context.

As an example, the libFuzzer fuzzing task uses the following:

* `setup`: A container with the libFuzzer target executable and any prerequisites
  (shared objects, DLLs, config files, etc)
* `crashes`: A container used to store any crashing input
* `inputs`: A container of an initial corpus of seeding input for the libFuzzer
  target. Any newly discovered inputs are also saved to this container. All
  files saved in the `inputs` container are bidirectionally synced with the blob
  store.
* `readonly_inputs`: An arbitrary set of additional input seed corpus containers.
  This container automatically pulls new files _from_ the blob store, but
  nothing saved to these containers on the fuzzing VM is synced _to_ the
  container.

Tasks can target a container for an input queue. As an example, the crash
reporting tasks queue off of specified `crashes` containers, processing files
iteratively from the queue.

## Considerations on naming Containers

Users can create arbitrary containers (see
[Container Name Requirements](https://docs.microsoft.com/en-us/rest/api/storageservices/Naming-and-Referencing-Containers--Blobs--and-Metadata#container-names)),
including the ability to set
[arbitrary metadata](https://docs.microsoft.com/en-us/rest/api/storageservices/setting-and-retrieving-properties-and-metadata-for-blob-resources)
for a container.

Templates use containers built from the context it's being used (setup) and a
namespaced GUID to enable automatic re-use of containers across multiple builds
of the same target. NOTE: A helper utility is available to craft namespaced
GUIDs `onefuzz utils namespaced_guid`.

As an example, setup and coverage containers are namespaced with the `project`,
`name`, `build`, and `platform` (Linux or Windows). All other containers
(inputs, crashes, reports, etc) use `project` and `name`.

Example:

The `libfuzzer` template with the project 'myproject', the name of 'mytarget',
and build of `build_1` on the Linux platform uses the following:

* oft-setup-fd4addc373f3551caf780e80abaaa658
* oft-coverage-fd4addc373f3551caf780e80abaaa658
* oft-inputs-d532156b72765c21be5a29f73718af7e
* oft-crashes-d532156b72765c21be5a29f73718af7e
* oft-reports-d532156b72765c21be5a29f73718af7e
* oft-unique-reports-d532156b72765c21be5a29f73718af7e
* oft-no-repro-d532156b72765c21be5a29f73718af7e

The same target, but `build_2` uses the following containers:

* oft-setup-270ee492f18c5f71a0a3e1cffcb98f77
* oft-coverage-270ee492f18c5f71a0a3e1cffcb98f77
* oft-inputs-d532156b72765c21be5a29f73718af7e
* oft-crashes-d532156b72765c21be5a29f73718af7e
* oft-reports-d532156b72765c21be5a29f73718af7e
* oft-unique-reports-d532156b72765c21be5a29f73718af7e
* oft-no-repro-d532156b72765c21be5a29f73718af7e

The only difference is a unique oft-setup container.

In these examples, `oft` stands for *O*ne*F*uzz *T*emplate "setup" container.
