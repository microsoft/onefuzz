
using System.Net.Http;

namespace Microsoft.OneFuzz.Service;

public class RequestAccess {
    private readonly Node _root = new();

    public record Rules(IReadOnlyList<Guid> AllowedGroupsIds);
    sealed record Node(
        // HTTP Method -> Rules
        Dictionary<HttpMethod, Rules> Rules,
        // Path Segment -> Node
        Dictionary<string, Node> Children) {
        public Node() : this(new(), new()) { }
    }

    private void AddUri(IEnumerable<HttpMethod> methods, string path, Rules rules) {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!segments.Any()) {
            return;
        }

        var currentNode = _root;
        var currentSegmentIndex = 0;

        while (currentSegmentIndex < segments.Length) {
            var currentSegment = segments[currentSegmentIndex];
            if (currentNode.Children.ContainsKey(currentSegment)) {
                currentNode = currentNode.Children[currentSegment];
                currentSegmentIndex++;
            } else {
                break;
            }
        }

        // we found a node matching this exact path
        // This means that there is an existing rule causing a conflict
        if (currentSegmentIndex == segments.Length) {
            var conflictingMethod = methods.FirstOrDefault(m => currentNode.Rules.ContainsKey(m));
            if (conflictingMethod is not null) {
                throw new RuleConflictException($"Conflicting rules on {conflictingMethod} {path}");
            }
        }

        while (currentSegmentIndex < segments.Length) {
            var currentSegment = segments[currentSegmentIndex];
            currentNode = currentNode.Children[currentSegment] = new Node();
            currentSegmentIndex++;
        }

        foreach (var method in methods) {
            currentNode.Rules[method] = rules;
        }
    }

    public static RequestAccess Build(IDictionary<string, ApiAccessRule> rules) {
        var result = new RequestAccess();
        foreach (var (endpoint, rule) in rules) {
            result.AddUri(rule.Methods.Select(x => new HttpMethod(x)), endpoint, new Rules(rule.AllowedGroups));
        }

        return result;
    }

    public Rules? GetMatchingRules(HttpMethod method, string path) {
        var segments = path.Split("/", StringSplitOptions.RemoveEmptyEntries);

        var currentNode = _root;
        _ = currentNode.Rules.TryGetValue(method, out var currentRule);

        foreach (var currentSegment in segments) {
            if (currentNode.Children.TryGetValue(currentSegment, out var node)) {
                currentNode = node;
            } else if (currentNode.Children.TryGetValue("*", out var starNode)) {
                currentNode = starNode;
            } else {
                break;
            }

            if (currentNode.Rules.TryGetValue(method, out var rule)) {
                currentRule = rule;
            }
        }

        return currentRule;
    }
}

public sealed class RuleConflictException : Exception {
    public RuleConflictException(string? message) : base(message) {
    }
}
