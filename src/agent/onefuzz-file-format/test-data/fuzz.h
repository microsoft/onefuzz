#include "lib/explode.h"

void check_hit_count(int hit) {
  if (hit > 3) {
    explode();
  }
}
