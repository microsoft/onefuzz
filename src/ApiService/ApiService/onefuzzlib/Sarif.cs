using System.IO;
using System.Net.Http;
using System.Text.Json;
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

public static class AsanHelper {

    static readonly Regex _frameRegex = new Regex(@"#(?<index>\d+) (?<address>0x[1234567890abcdef]+) in (?<func>.+) \(?(?<file>[^:()+]+)(:(?<line>\d+))?(:(?<column>\d+))?\)?", RegexOptions.ExplicitCapture | RegexOptions.RightToLeft);
    public static AsanFrame? TryParseAsanFrame(string frame) {
        var match = _frameRegex.Match(frame);
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

    static readonly Dictionary<string, string> _asanErrorCodeMapping = new Dictionary<string, string> {

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
        return _asanErrorCodeMapping.GetValueOrDefault(error, "AS900");
    }

}


public class SarifGenerator{
    static readonly Uri _asanErrorUrl = new Uri("https://github.com/google/sanitizers/wiki/AddressSanitizer");

     
    /// <summary>
    /// Builds a URI that canonicalize the path. 
    /// This is needed because the default serializer used in sarif log (newtonsoft)
    /// Will use the Original string value of the url and that value will contains the . segement if present
    /// However ,  dot segment are not authorized by the sarif validator
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    static Uri BuildUri(string path) {
        return new UriBuilder(path).Uri;
    }

    public static Location GetLocationFromGenericFrame(string rootPath, string frame) {
        var rootUri = new System.Uri(rootPath);
        var splitted = frame.Split(" ");
        if (splitted.Length == 4) {
            var index = splitted[0];
            var address = splitted[1];
            var funcName = splitted[2];
            var path = splitted[3].Trim();
            var uri = BuildUri($"file://{path}");

            return new Location{
                Message = new Message { Text = frame },
                PhysicalLocation = new PhysicalLocation
                {
                    ArtifactLocation =
                        rootUri.IsBaseOf(uri)
                            ? new ArtifactLocation { Uri = rootUri.MakeRelativeUri(uri),  UriBaseId = "SRCROOT" }
                            : new ArtifactLocation { Uri = uri}
                }
            };
        }
        else
        {
            return new Location { Message = new Message { Text = frame}};
        }
    }


    public static Location GetLocationFromAsanFrame(string rootPath, string frame)
    {
        var rootUri = new System.Uri($"file://{rootPath}");

        var asanFrame = AsanHelper.TryParseAsanFrame(frame);
        if (asanFrame != null)
        {
            var pathUri = BuildUri($"file://{asanFrame.File}");
            return
                new Location
                {
                    Message = new Message { Text = frame },
                    PhysicalLocation = new PhysicalLocation
                    {
                        ArtifactLocation =
                            rootUri.IsBaseOf(pathUri)
                                ? new ArtifactLocation { Uri = rootUri.MakeRelativeUri(pathUri), UriBaseId = "SRCROOT" }
                                : new ArtifactLocation { Uri = pathUri },
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
    public static List<StackFrame> GetStackFrames(string rootPath, Report report)
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



    public static SarifLog ToSarif(string inputRootPath, Report report)
    {
        // adding the directory speratator at the end
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
                            HelpUri = _asanErrorUrl
                        };


        var sarifLog = new SarifLog
        {
            Runs = new List<Run>{
                    new Run{
                        Tool = new Tool{
                                Driver = new ToolComponent{
                                        Name = report.ToolName ?? "Onefuzz", // TODO: this might need to be more specific
                                        SemanticVersion = report.OnefuzzVersion ?? "0.0.1", // TODO: get the onfuzz version
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


    public async System.Threading.Tasks.Task<SarifLog> Validate(SarifLog sarifLog) {
        using var mem = new MemoryStream();
        await using var writer = new StreamWriter(mem, leaveOpen: true);
        using var jsonWriter = new Newtonsoft.Json.JsonTextWriter(writer) { Formatting = Newtonsoft.Json.Formatting.Indented};
        var jsonSerializer = new Newtonsoft.Json.JsonSerializer();
        jsonSerializer.Serialize(jsonWriter, sarifLog);
        await jsonWriter.FlushAsync();
        mem.Position = 0;
        using var content = new StreamContent(mem);
        using var client = new HttpClient();
        var multiForm = new MultipartFormDataContent();
        multiForm.Add(content, "postedFiles", "test.sarif");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new UriBuilder("https://sarifweb.azurewebsites.net/Validation/ValidateFiles").Uri)
        {
            Content = multiForm,
        };

        var result = await client.SendAsync(httpRequest);
        
        var resultContent = await JsonDocument.ParseAsync(result.Content.ReadAsStream());
        var sarifString = resultContent.RootElement.GetProperty("resultsLogContents").GetString();
        using var mem2 = new MemoryStream();
        await using var writer2 = new StreamWriter(mem2);
        await writer2.WriteAsync(sarifString);
        await writer2.FlushAsync();
        mem2.Position = 0 ;
        var validationResult = SarifLog.Load(mem2);
        return validationResult;
    }
}

public static class SarifLogExtensions
{
    public static string ToJsonString(this SarifLog sarifLog)
    {
        using var mem = new MemoryStream();
        using var writer = new StreamWriter(mem, leaveOpen: true);
        sarifLog.Save(writer);
        return System.Text.Encoding.UTF8.GetString(mem.ToArray(), 0, (int) mem.Length);
    }

    public static async System.Threading.Tasks.Task<SarifLog> Validate(this SarifLog sarifLog) {
        using var mem = new MemoryStream();
        await using var writer = new StreamWriter(mem, leaveOpen: true);
        using var jsonWriter = new Newtonsoft.Json.JsonTextWriter(writer) { Formatting = Newtonsoft.Json.Formatting.Indented };
        var jsonSerializer = new Newtonsoft.Json.JsonSerializer();
        jsonSerializer.Serialize(jsonWriter, sarifLog);
        await jsonWriter.FlushAsync();
        mem.Position = 0;
        using var content = new StreamContent(mem);
        using var client = new HttpClient();
        var multiForm = new MultipartFormDataContent();
        multiForm.Add(content, "postedFiles", "test.sarif");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new UriBuilder("https://sarifweb.azurewebsites.net/Validation/ValidateFiles").Uri) {
            Content = multiForm,
        };

        var result = await client.SendAsync(httpRequest);

        var resultContent = await JsonDocument.ParseAsync(result.Content.ReadAsStream());
        var sarifString = resultContent.RootElement.GetProperty("resultsLogContents").GetString();
        using var mem2 = new MemoryStream();
        await using var writer2 = new StreamWriter(mem2);
        await writer2.WriteAsync(sarifString);
        await writer2.FlushAsync();
        mem2.Position = 0;
        var validationResult = SarifLog.Load(mem2);
        return validationResult;
    }
}
