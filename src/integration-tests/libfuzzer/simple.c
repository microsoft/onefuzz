// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <stdbool.h>
#include <string.h>

// allow an argument --write_test_file=xxx.txt to be set
// which is useful for exercising some OneFuzz features in integration tests
int LLVMFuzzerInitialize(int *argc, char ***argv) {
  const int num_args = *argc;
  char** args = *argv;

  for (int i = 0; i < num_args; ++i) {
    // look for argument starting with --write_test_file=
    const char* arg_name = "--write_test_file=";
    if (strncmp(args[i], arg_name, strlen(arg_name)) == 0) {
      // extract filename part
      const char* file_name = args[i] + strlen(arg_name);
      // write file
      FILE* output = fopen(file_name, "a");
      if (!output) {
        perror("failed to open file");
        return -1;
      }

      fputs("Hello from simple fuzzer\n", output);
      fclose(output);
      break;
    }
  }

  return 0;
}

int LLVMFuzzerTestOneInput(const uint8_t *data, size_t len) {
  int cnt = 0;

  if (len < 4) {
    return 0;
  }

  if (data[0] == 'x') { cnt++; }
  if (data[1] == 'y') { cnt++; }
  if (data[2] == 'z') { cnt++; }

  if (cnt >= 3) {
    switch (data[3]) {
      case '0': {
        // segv
        int *p = NULL; *p = 123;
        break;
      }
      case '1': {
        // stack-buffer-underflow
        int* p = &cnt - 32; for (int i = 0; i < 32; i++) { *(p + i) = 0; }
        break;
      }
      case '2': {
        // stack-buffer-overflow 
        int* p = &cnt + 32; for (int i = 0; i < 32; i++) { *(p - i) = 0; }
        break;
      }
      case '3': {
        // bad-free
        int *p = &cnt; free(p);
        break;
      }
      case '4': {
        // double-free
        int* p = (int *) malloc(sizeof(int)); free(p); free(p);
        break;
      }
      case '5': {
        // heap-use-after-free
        int* p = (int *) malloc(sizeof(int)); free(p); *p = 123;
        break;
      }
      case '6': {
        // heap-buffer-overflow
        int* p = (int *) malloc(8 * sizeof(int)); for (int i = 0; i < 32; i++) { *(p + i) = 0; }
        break;
      }
      case '7': {
        // fpe
        int x = 0; int y = 123 / x;
        break;
      }
      case '8': {
        abort();
        break;
      }
    }
  }

  return 0;
}
