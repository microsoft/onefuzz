#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

#include "fuzz.h"

int LLVMFuzzerTestOneInput(uint8_t *data, size_t len) {
  if (len < 4) { return 0; }

  int hit = 0;

  // Multiple statements per line.
  if (data[0] == 'b') { hit++; }

  // One statement per line.
  if (data[1] == 'a') {
    hit++;
  }

  // Access separate from comparison.
  char c = data[2];
  if (c == 'd') {
    hit++;
  }

  // Switch.
  switch (data[3]) {
    case '!': {
      hit++;
      break;
    }
    default: {
      // Do nothing.
    }
  }

  if (len > 4 && data[4] == '!') {
    // Also used in `check_hit_count()`.
    explode();
  }

  check_hit_count(hit);

  return 0;
}
