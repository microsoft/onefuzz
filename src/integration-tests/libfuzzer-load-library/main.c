// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include <windows.h> 
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

int (*fuzz_func)(const uint8_t *data, size_t size);

int LLVMFuzzerInitialize(int *argc, char ***argv)
{
    HINSTANCE handle; 
    
    printf("initialize\n");

    handle = LoadLibrary(TEXT("bad.dll")); 
    if (!handle)
    {
        printf("can't open dll\n");
        exit(EXIT_FAILURE);
    }

    fuzz_func = (int (*)(const uint8_t *data, size_t size))GetProcAddress(handle, "func");
    if (fuzz_func == NULL) {
        printf("unable to load fuzz func\n");
        exit(EXIT_FAILURE);
    }

    return 0;
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size)
{
    assert(fuzz_func != NULL);
    return fuzz_func(data, size);
}
