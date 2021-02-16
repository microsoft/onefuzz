using System;
namespace Problems {
  public static class Problems {
    public static void Func(ReadOnlySpan<byte> data) {
      var count = 0;
      if (data.Length < 4) {
         return;
      }
      if (data[0] == 0) { count++; } 
      if (data[1] == 1) { count++; }
      if (data[2] == 2) { count++; }
      if (count >= 3) { throw new Exception("this is bad"); }
    }
  }
}
