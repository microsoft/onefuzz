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
      if (count >= 2) { throw new Exception("this is bad"); }
    }
  }
}
