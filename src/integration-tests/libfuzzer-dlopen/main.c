// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include <assert.h>
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

int (*fuzz_func)(const uint8_t *data, size_t size);

int LLVMFuzzerInitialize(int *argc, char ***argv)
{
    printf("initilize\n");
    void *handle;
    int (*b)(void);
    char *error;

    handle = dlopen("libbad.so", RTLD_LAZY);
    if (!handle)
    {
        printf("can't open %s", dlerror());
        return 1;
    }
    fuzz_func = (int (*)(const uint8_t *data, size_t size))dlsym(handle, "func");
    error = dlerror();
    if (error != NULL)
    {
        printf("%s\n", error);
        exit(EXIT_FAILURE);
    }
    return 0;
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size)
{
    assert(fuzz_func != NULL);
    return fuzz_func(data, size);
}
