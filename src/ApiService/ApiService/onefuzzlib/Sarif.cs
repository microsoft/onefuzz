using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Sarif;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace  Microsoft.OneFuzz.Service;
public record AsanFrame (
    int Index,
    string Address,
    string Func,
    string File,
    int? Line
);

public class AsanHelper {

    static Regex FrameRegex = new Regex(@"#(?<index>\d+) (?<address>0x[1234567890abcdef]+) in (?<func>.+) (?<file>[^:]+)(:(?<line>\d+))?(:(?<column>\d+))?", RegexOptions.ExplicitCapture | RegexOptions.RightToLeft);
    public static AsanFrame? TryParseAsanFrame(string frame) {
        var match = FrameRegex.Match(frame);
        if (!match.Success) {
            return null;
        }

        return new AsanFrame(
            Index: int.Parse(match.Groups["index"].Value),
            Address: match.Groups["address"].Value,
            Func: match.Groups["func"].Value,
            File: match.Groups["file"].Value.Trim('(', ')'),
            Line: match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value) : null
        );
    }

    static Dictionary<string, string> AsanErrorCodeMapping = new Dictionary<string, string> {

        {"use-after-free", "AS001"},
        {"heap-buffer-overflow", "AS002"},
        {"stack-buffer-overflow", "AS003"},
        {"global-buffer-overflow", "AS004"},
        {"use-after-return", "AS005"},
        {"use-after-scope", "AS006"},
        {"initialization-order-bugs", "AS007"},
        {"memory-leaks", "AS008"},
    };

    public static  string GetAsantErrorCode(string error) {
        return AsanErrorCodeMapping.GetValueOrDefault(error, "AS900");
    }

}



public class SarifGenerator{
    Uri AsanErrorUrl = new Uri("https://github.com/google/sanitizers/wiki/AddressSanitizer");

    public Location GetLocationFromGenericFrame(string rootPath, string frame) {
        var rootUri = new System.Uri(rootPath);
        var splitted = frame.Split(" ");
        if (splitted.Length == 4) {
            var index = splitted[0];
            var address = splitted[1];
            var funcName = splitted[2];
            var path = splitted[3].Trim();
            var uri = Path.IsPathRooted(path)
                        ?  rootUri.MakeRelativeUri(new System.Uri(path))
                        : new System.Uri(path, System.UriKind.Relative);

            return new Location{
                Message = new Message { Text = frame },
                PhysicalLocation = new PhysicalLocation
                {
                    ArtifactLocation =
                        Path.IsPathRooted(uri.OriginalString)
                            ? new ArtifactLocation { Uri = new System.Uri(path, System.UriKind.Absolute) }
                            : new ArtifactLocation { Uri = uri, UriBaseId = "SRCROOT"}
                }
            };
        }
        else
        {
            return new Location { Message = new Message { Text = frame}};
        }
    }


    public Location GetLocationFromAsanFrame(string rootPath, string frame)
    {
        var rootUri = new System.Uri(rootPath.StartsWith("/") ? $"/{rootPath}": rootPath);

        var asanFrame = AsanHelper.TryParseAsanFrame(frame);
        if (asanFrame != null)
        {

            var relativePath = Path.IsPathRooted(asanFrame.File)
                        ? Path.GetRelativePath(rootPath, asanFrame.File) //rootUri.MakeRelativeUri(new System.Uri(asanFrame.File))
                        : asanFrame.File ;//new System.Uri(asanFrame.File.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), System.UriKind.Relative);

            return
                new Location
                {
                    Message = new Message { Text = frame },
                    PhysicalLocation = new PhysicalLocation
                    {
                        ArtifactLocation =
                            Path.IsPathRooted(relativePath)
                                ? new ArtifactLocation { Uri = new System.Uri(asanFrame.File, System.UriKind.Absolute) }
                                : new ArtifactLocation { Uri = new Uri(relativePath, System.UriKind.Relative), UriBaseId = "SRCROOT" },
                        Region = asanFrame.Line switch { int l => new Region { StartLine = l }, null => null },
                        // this line causes an issue in the validator
                        //ContextRegion = asanFrame.line switch { int l => new Region { StartLine = Math.Max(0, l - 1), EndLine = l }, _ => null }
                    }
                };
        }
        else
        {
            return new Location { Message = new Message { Text = frame } };
        }
    }


    public List<StackFrame> GetStackFrames(string rootPath, Report report)
    {

        var parsedLocation = report.AsanLog switch
        {
            string _ => report.CallStack.Select(cs => GetLocationFromAsanFrame(rootPath, cs)),
            _ => report.CallStack.Select(cs => GetLocationFromGenericFrame(rootPath, cs))
        };

        var frames =
            parsedLocation
            .Select(x => new StackFrame{Location = x})
            .ToList();
        return frames;
    }



    public SarifLog ToSarif(string inputRootPath, Report report)
    {
        var rootPath =
                System.IO.Path.EndsInDirectorySeparator(inputRootPath)
                    ? inputRootPath
                    : $"{inputRootPath}{System.IO.Path.DirectorySeparatorChar}";


        var frames = GetStackFrames(rootPath, report);

        ReportingDescriptor rule = report.AsanLog == null
            ? rule = new ReportingDescriptor
                        {
                            Id = report.CrashType,
                            Name = CaseConverter.SnakeToPascal(report.CrashType),
                            HelpUri = new System.Uri("about:blank")
                        }
            : rule = new ReportingDescriptor
                        {
                            Id = AsanHelper.GetAsantErrorCode(report.CrashType),
                            Name = CaseConverter.SnakeToPascal(report.CrashType),
                            HelpUri = AsanErrorUrl
                        };


        var sarifLog = new SarifLog
        {
            Runs = new List<Run>{
                    new Run{
                        Tool = new Tool{
                                Driver = new ToolComponent{
                                        Name = report.ToolName, // TODO: this might need to be more specific
                                        SemanticVersion = "0.0.1", // TODO: get the onfuzz version
                                        Organization = "Microsoft",
                                        Product = "OneFuzz",
                                        ShortDescription = new MultiformatMessageString{Text = "Onefuzz fuzzing platform"},
                                        InformationUri = new System.Uri("https://github.com/microsoft/onefuzz"),
                                        Rules = new List<ReportingDescriptor>{rule}
                                    }
                            },
                        Results =
                            new List<Result> {new Result{
                                    RuleId = rule.Id,
                                    Stacks = new List<Microsoft.CodeAnalysis.Sarif.Stack> {new Microsoft.CodeAnalysis.Sarif.Stack{Frames = frames}},
                                    Message = new Message{Id = "default"},
                                    Locations =
                                        frames
                                            .Select(f => f.Location)
                                            .Take(1)
                                            .ToList()
                                }}
                                ,
                        Artifacts =
                                report.AsanLog switch {
                                    string asanLog => new List<Artifact>{ new Artifact{
                                                Description = new Message{Text = "ASAN log"},
                                                Contents = new ArtifactContent{Text = asanLog}
                                            }},
                                    null => null
                                },

                        OriginalUriBaseIds = new Dictionary<string, ArtifactLocation> { {"SRCROOT", new ArtifactLocation{Uri =new  System.Uri(rootPath.StartsWith("/")? $"/{rootPath}": rootPath )} }},
                        VersionControlProvenance = new List<VersionControlDetails> {
                                    new VersionControlDetails{
                                        RepositoryUri = new System.Uri("https://httpstat.us/200"),
                                        RevisionId = "none",
                                        Branch = "none",
                                        MappedTo = new ArtifactLocation{UriBaseId = "SRCROOT"}
                                    }}
                    }
                }
        };


        return sarifLog;
    }
}
