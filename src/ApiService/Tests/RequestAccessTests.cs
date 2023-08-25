using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class RequestAccessTests {

    [Fact]
    public void TestEmpty() {
        var requestAccess1 = RequestAccess.Build(new Dictionary<string, ApiAccessRule>());
        var rules1 = requestAccess1.GetMatchingRules(HttpMethod.Get, "a/b/c");
        Assert.Null(rules1);

        var requestAccess2 = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "a/b/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new[]{Guid.NewGuid()})}});

        var rules2 = requestAccess2.GetMatchingRules(HttpMethod.Get, "");
        Assert.Null(rules2);
    }

    [Fact]
    public void TestExactMatch() {
        var guid1 = Guid.NewGuid();
        var requestAccess = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "a/b/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid1})}});

        var rules1 = requestAccess.GetMatchingRules(HttpMethod.Get, "a/b/c");
        Assert.NotNull(rules1);
        var foundGuid = Assert.Single(rules1!.AllowedGroupsIds);
        Assert.Equal(guid1, foundGuid);

        var rules2 = requestAccess.GetMatchingRules(HttpMethod.Get, "b/b/e");
        Assert.Null(rules2);
    }

    [Fact]
    public void TestWildcard() {
        var guid1 = Guid.NewGuid();
        var requestAccess = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "b/*/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid1})}});

        var rules = requestAccess.GetMatchingRules(HttpMethod.Get, "b/b/c");
        Assert.NotNull(rules);
        var foundGuid = Assert.Single(rules!.AllowedGroupsIds);
        Assert.Equal(guid1, foundGuid);
    }

    [Fact]
    public void TestAddingRuleOnSamePath() {
        _ = Assert.Throws<RuleConflictException>(() => {
            var guid1 = Guid.NewGuid();
            _ = RequestAccess.Build(
                new Dictionary<string, ApiAccessRule>{
                    { "a/b/c", new ApiAccessRule(
                        Methods: new[]{"get"},
                        AllowedGroups: new []{guid1})},
                    { "a/b/c/", new ApiAccessRule(
                        Methods: new[]{"get"},
                        AllowedGroups: Array.Empty<Guid>())}});
        });
    }

    [Fact]
    public void TestPriority() {
        // The most specific rules takes priority over the ones containing a wildcard
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var requestAccess = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "a/*/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid1})},
                { "a/b/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new[]{guid2})}});

        var rules = requestAccess.GetMatchingRules(HttpMethod.Get, "a/b/c");
        Assert.NotNull(rules);
        Assert.Equal(guid2, Assert.Single(rules!.AllowedGroupsIds)); // should match the most specific rule
    }

    [Fact]
    public void TestInheritRule() {
        // if a path has no specific rule. it inherits from the parents
        // /a/b/c inherit from a/b
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var requestAccess = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "a/b/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid1})},
                { "f/*/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new[]{guid2})},
                { "a/b", new ApiAccessRule(
                    Methods: new[]{"post"},
                    AllowedGroups: new []{guid3})}});

        // should inherit a/b/c
        var rules1 = requestAccess.GetMatchingRules(HttpMethod.Get, "a/b/c/d");
        Assert.NotNull(rules1);
        Assert.Equal(guid1, Assert.Single(rules1!.AllowedGroupsIds));

        // should inherit f/*/c
        var rules2 = requestAccess.GetMatchingRules(HttpMethod.Get, "f/b/c/d");
        Assert.NotNull(rules2);
        Assert.Equal(guid2, Assert.Single(rules2!.AllowedGroupsIds));

        // post should inherit a/b
        var rules3 = requestAccess.GetMatchingRules(HttpMethod.Post, "a/b/c/d");
        Assert.NotNull(rules3);
        Assert.Equal(guid3, Assert.Single(rules3!.AllowedGroupsIds));
    }

    [Fact]
    public void TestOverrideRule() {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var requestAccess = RequestAccess.Build(
            new Dictionary<string, ApiAccessRule>{
                { "a/b/c", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid1})},
                { "a/b/c/d", new ApiAccessRule(
                    Methods: new[]{"get"},
                    AllowedGroups: new []{guid2})}});

        // should inherit a/b/c
        var rules1 = requestAccess.GetMatchingRules(HttpMethod.Get, "a/b/c");
        Assert.NotNull(rules1);
        Assert.Equal(guid1, Assert.Single(rules1!.AllowedGroupsIds));

        // should inherit a/b/c/d
        var rules2 = requestAccess.GetMatchingRules(HttpMethod.Get, "a/b/c/d");
        Assert.NotNull(rules2);
        Assert.Equal(guid2, Assert.Single(rules2!.AllowedGroupsIds));
    }
}
