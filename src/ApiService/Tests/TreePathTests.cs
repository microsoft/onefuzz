using System.Collections.Generic;
using System.Linq;
using Microsoft.OneFuzz.Service;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Xunit;

namespace Tests;

// This might be a good candidate for property based testing
// https://fscheck.github.io/FsCheck//QuickStart.html
public class TreePathTests {
    private static IEnumerable<string> SplitPath(string path) {
        return path.Split('\\');
    }

    private static WorkItemClassificationNode MockTreeNode(IEnumerable<string> path, TreeNodeStructureType structureType) {
        var root = new WorkItemClassificationNode() {
            Name = path.First(),
            StructureType = structureType
        };

        var current = root;
        foreach (var segment in path.Skip(1)) {
            var child = new WorkItemClassificationNode {
                Name = segment
            };
            current.Children = new[] { child };
            current = child;
        }

        return root;
    }


    [Fact]
    public void TestValidPath() {
        var path = SplitPath(@"project\foo\bar\baz");
        var root = MockTreeNode(path, TreeNodeStructureType.Area);

        var result = Ado.ValidateTreePath(path, root);

        Assert.True(result.IsOk);
    }

    [Fact]
    public void TestNullTreeNode() { // A null tree node indicates an invalid ADO project was used in the query
        var path = SplitPath(@"project\foo\bar\baz");

        var result = Ado.ValidateTreePath(path, null);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PROJECT, result.ErrorV!.Code);
        Assert.Contains("ADO project doesn't exist", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathPartTooLong() {
        var path = SplitPath(@"project\foo\barbazquxquuxcorgegraultgarplywaldofredplughxyzzythudbarbazquxquuxcorgegraultgarplywaldofredplughxyzzythudbarbazquxquuxcorgegraultgarplywaldofredplughxyzzythudbarbazquxquuxcorgegraultgarplywaldofredplughxyzzythudbarbazquxquuxcorgegraultgarplywaldofredplughxyzzythud\baz");
        var root = MockTreeNode(path, TreeNodeStructureType.Iteration);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("too long", result.ErrorV!.Errors![0]);
    }

    [Theory]
    [InlineData("project/foo/bar/baz")]
    [InlineData("project\\foo.\\bar\\baz")]
    [InlineData("project\\foo\f\\bar\\baz")]
    public void TestPathContainsInvalidChar(string invalidPath) {
        var path = SplitPath(invalidPath);
        var treePath = SplitPath(@"project\foo\bar\baz");
        var root = MockTreeNode(treePath, TreeNodeStructureType.Area);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("invalid character", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathContainsUnicodeControlChar() {
        var path = SplitPath("project\\foo\\ba\u0005r\\baz");
        var root = MockTreeNode(path, TreeNodeStructureType.Area);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("unicode control character", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathTooDeep() {
        var path = SplitPath(@"project\foo\bar\baz\qux\quux\corge\grault\garply\waldo\fred\plugh\xyzzy\thud");
        var root = MockTreeNode(path, TreeNodeStructureType.Area);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("levels deep", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathWithoutProjectName() {
        var path = SplitPath(@"foo\bar\baz");
        var treePath = SplitPath(@"project\foo\bar\baz");
        var root = MockTreeNode(treePath, TreeNodeStructureType.Iteration);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("start with the project name", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathWithInvalidChild() {
        var path = SplitPath(@"project\foo\baz");
        var treePath = SplitPath(@"project\foo\bar");
        var root = MockTreeNode(treePath, TreeNodeStructureType.Iteration);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("not a valid child", result.ErrorV!.Errors![0]);
    }

    [Fact]
    public void TestPathWithExtraChild() {
        var path = SplitPath(@"project\foo\bar\baz");
        var treePath = SplitPath(@"project\foo\bar");
        var root = MockTreeNode(treePath, TreeNodeStructureType.Iteration);

        var result = Ado.ValidateTreePath(path, root);

        Assert.False(result.IsOk);
        Assert.Equal(ErrorCode.ADO_VALIDATION_INVALID_PATH, result.ErrorV!.Code);
        Assert.Contains("has no children", result.ErrorV!.Errors![0]);
    }
}
