#include <string.h>
#include <stdio.h>
#include <stdlib.h>

#ifdef _WIN64
#include <io.h>
#pragma GCC diagnostic ignored "-Wdeprecated-declarations"
#define STDIN_FILENO _fileno(stdin)
#define read _read
#else
#include <unistd.h>
#endif

#define SIZE 8192
#define BUF_SIZE 32

int check(const char *data, size_t len)
{
  char buf[BUF_SIZE];
  memset(buf, 0, BUF_SIZE);
  strncpy(buf, data, len); // BUG - This incorrectly uses length of src, not dst

  // do something to ensure this isn't optimized away
  int buflen = strlen(buf);
  for (int i = 0; i <= ((buflen % 2 == 1) ? buflen - 1 : buflen) / 2; i++)
  {
    if (buf[i] != buf[buflen - 1 - i])
    {
      printf("not palindrome: ");
      printf(buf);
      printf("\n");
      break;
    }
  }

  return 0;
}

int from_stdin()
{
  char input[SIZE] = {0};
  int size = read(STDIN_FILENO, input, SIZE);
  return check(input, size);
}

int from_file(char *filename)
{
  FILE *infile;
  char *buffer;
  long length;
  int result;

  infile = fopen(filename, "r");

  if (infile == NULL)
    return 1;

  fseek(infile, 0L, SEEK_END);
  length = ftell(infile);

  fseek(infile, 0L, SEEK_SET);
  buffer = calloc(length, sizeof(char));
  if (buffer == NULL)
    return 1;

  length = fread(buffer, sizeof(char), length, infile);
  fclose(infile);

  result = check(buffer, length);
  free(buffer);
  return result;
}

int main(int argc, char **argv)
{

  if (argc == 1)
  {
    return from_stdin();
  }
  else if (argc > 1)
  {
    return from_file(argv[1]);
  }
}
