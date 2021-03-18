#include <stdlib.h>
int LLVMFuzzerTestOneInput(char *data, size_t len) { 
   if (len != 1) { return 0; }  
   if (data[0] == '1') { abort(); } // TEST1
   if (data[0] == '2') { abort(); } // TEST2
   if (data[0] == '3') { abort(); } // TEST3
   if (data[0] == '4') { abort(); } // TEST4
   if (data[0] == '5') { abort(); } // TEST5
   if (data[0] == '6') { abort(); } // TEST6
   if (data[0] == '7') { abort(); } // TEST7
   if (data[0] == '8') { abort(); } // TEST8
   return 0;
}
