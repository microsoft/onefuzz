using SharpFuzz;
namespace Wrapper {
  public class Program {
    public static void Main(string[] args) {
      Fuzzer.LibFuzzer.Run(stream => { Problems.Problems.Func(stream); });
    }
  }
}
