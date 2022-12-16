void explode() {
  int *ptr = (int *) 0xdead;
  *ptr = 0x123;
}
