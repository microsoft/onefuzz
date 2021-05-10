// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include <stdint.h>
#include <stdlib.h>

__declspec(dllexport) int func(const uint8_t *data, size_t len) {
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
        int* p = malloc(sizeof(int)); free(p); free(p);
        break;
      }
      case '5': {
        // heap-use-after-free
        int* p = malloc(sizeof(int)); free(p); *p = 123;
        break;
      }
      case '6': {
        // heap-buffer-overflow
        int* p = malloc(8 * sizeof(int)); for (int i = 0; i < 32; i++) { *(p + i) = 0; }
        break;
      }
      case '7': {
        // fpe
        int x = 0; int y = 123 / x;
        break;
      }
    }
  }

  return 0;
}
