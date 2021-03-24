// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include <stdint.h>
#include <stdlib.h>


int LLVMFuzzerTestOneInput(const uint8_t *data, size_t len) {
  int cnt = 0;

  if (len < 3) {
    return 0;
  }

  if (data[0] == 'x') { cnt++; }
  if (data[1] == 'y') { cnt++; }
  if (data[2] == 'z') { cnt++; }

#ifndef FIXED
  if (cnt >= 3) {
     abort();
  }
#endif

  return 0;
}
