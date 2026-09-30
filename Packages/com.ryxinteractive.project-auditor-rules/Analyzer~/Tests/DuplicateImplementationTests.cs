// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace RyxInteractive.ProjectAuditorRules.Tests;

public sealed class DuplicateImplementationTests
{
    private static string Method(string name, string parameter = "values", string local = "total",
        string index = "i", string change = "", int repeats = 6) => $$"""
        private static int {{name}}(int[] {{parameter}})
        {
            int {{local}} = 0;
            for (int {{index}} = 0; {{index}} < {{parameter}}.Length; {{index}}++)
            {
                if ({{parameter}}[{{index}}] > 0)
                {
                    {{string.Join("\n", Enumerable.Repeat($"{local} += {parameter}[{index}];", repeats))}}
                }
            }
            {{change}}
            return {{local}};
        }
        """;

    private static SourceFile Source(string methods, string type = "C", string path = "Assets/Fixture.cs")
        => new(path, $"public class {type} {{ {methods} }}");

    [Fact]
    public void Renamed_parameters_and_locals_match_across_types_and_files()
    {
        using var host = new AnalyzerTestHost();
        var first = Source(Method("A"), "First", "Assets/A.cs");
        var second = Source(Method("B", "numbers", "sum", "j"), "Second", "Assets/B.cs");
        var diagnostic = Assert.Single(host.AnalyzeDuplicates(second, first));
        Assert.Equal(DiagnosticIds.DuplicateImplementation, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.EndsWith("B.cs", diagnostic.Location.SourceTree!.FilePath);
        Assert.EndsWith("A.cs", Assert.Single(diagnostic.AdditionalLocations).SourceTree!.FilePath);
    }

    [Fact]
    public void Three_copies_report_twice_with_deterministic_original()
    {
        using var host = new AnalyzerTestHost();
        for (int run = 0; run < 3; run++)
        {
            var diagnostics = host.AnalyzeDuplicates(Source(Method("A") + Method("B") + Method("Third")));
            Assert.Equal(2, diagnostics.Length);
            Assert.All(diagnostics, d => Assert.Contains("C.A", d.GetMessage()));
            Assert.Single(diagnostics.Select(d => d.AdditionalLocations[0].SourceSpan).Distinct());
        }
    }

    [Theory]
    [InlineData(" > 0", " >= 0")]
    [InlineData(" > 0", " > 1")]
    [InlineData("total += values[i]", "total -= values[i]")]
    [InlineData("return total", "return total + 1")]
    [InlineData("int total = 0", "int total = 1")]
    [InlineData("total += values[i]", "total += i")]
    public void Semantic_changes_do_not_match(string oldText, string newText)
    {
        using var host = new AnalyzerTestHost();
        Assert.Empty(host.AnalyzeDuplicates(Source(Method("A") + Method("B").Replace(oldText, newText))));
    }

    [Fact]
    public void Different_called_symbols_fields_and_checked_context_do_not_match()
    {
        using var host = new AnalyzerTestHost();
        string helpers = "private static int X(int n) => n; private static int Y(int n) => n;" +
            "private static int x, y;";
        foreach (var pair in new[] { ("total = X(total);", "total = Y(total);"),
            ("total += x;", "total += y;"),
            ("checked { total++; }", "unchecked { total++; }") })
            Assert.Empty(host.AnalyzeDuplicates(Source(helpers + Method("A", change: pair.Item1) +
                Method("B", change: pair.Item2))));
    }

    [Fact]
    public void Same_called_symbol_matches()
    {
        using var host = new AnalyzerTestHost();
        Assert.Single(host.AnalyzeDuplicates(Source("private static int X(int n) => n;" +
            Method("A", change: "total = X(total);") + Method("B", change: "total = X(total);"))));
    }

    [Theory]
    [InlineData("private static", "public static")]
    [InlineData("private static", "private")]
    [InlineData("private static int", "private static async System.Threading.Tasks.Task<int>")]
    [InlineData("return total;", "return total + nameof(values).Length;")]
    [InlineData("return total;", "System.Func<int> f = () => total; return f();")]
    [InlineData("return total;", "int Local() => total; return Local();")]
    [InlineData("return total;", "dynamic d = total; return d;")]
    public void Unsupported_or_ineligible_methods_are_skipped(string oldText, string newText)
    {
        using var host = new AnalyzerTestHost();
        Assert.Empty(host.AnalyzeDuplicates(Source((Method("A") + Method("B")).Replace(oldText, newText))));
    }

    [Fact]
    public void Caller_info_invocations_are_skipped()
    {
        using var host = new AnalyzerTestHost();
        string helper = "private static void Log([System.Runtime.CompilerServices.CallerMemberName] string name = null) {}";
        Assert.Empty(host.AnalyzeDuplicates(Source(helper + Method("A", change: "Log();") +
            Method("B", change: "Log();"))));
    }

    [Fact]
    public void Recursive_and_yield_methods_are_skipped()
    {
        using var host = new AnalyzerTestHost();
        Assert.Empty(host.AnalyzeDuplicates(Source(Method("A", change: "total += A(values);") +
            Method("B", change: "total += B(values);"))));
        var iterator = (Method("A") + Method("B"))
            .Replace("static int ", "static System.Collections.Generic.IEnumerable<int> ")
            .Replace("return total;", "yield return total;");
        Assert.Empty(host.AnalyzeDuplicates(Source(iterator)));
    }

    [Fact]
    public void Short_methods_and_straight_line_boilerplate_are_skipped()
    {
        using var host = new AnalyzerTestHost();
        Assert.Empty(host.AnalyzeDuplicates(Source("private static int A(int n) => n + 1; private static int B(int n) => n + 1;")));
        var statements = string.Join("\n", Enumerable.Repeat("n++;", 50));
        Assert.Empty(host.AnalyzeDuplicates(Source($"private static int A(int n) {{ {statements} return n; }}" +
            $"private static int B(int n) {{ {statements} return n; }}")));
    }

    [Fact]
    public void Explicit_operation_threshold_is_inclusive()
    {
        using var host = new AnalyzerTestHost();
        // Find adjacent valid fixtures at 39 and 40 operations. Appending an empty
        // statement adds exactly one explicit operation without changing flow.
        var baseMethod = Method("A", repeats: 0);
        int count = CountOperations(Source(baseMethod).Source);
        Assert.True(count <= 39);
        var below = Method("A", change: new string(';', 39 - count), repeats: 0);
        var at = Method("A", change: new string(';', 40 - count), repeats: 0);
        Assert.Equal(39, CountOperations(Source(below).Source));
        Assert.Equal(40, CountOperations(Source(at).Source));
        Assert.Empty(host.AnalyzeDuplicates(Source(below + below.Replace(" A(", " B("))));
        Assert.Single(host.AnalyzeDuplicates(Source(at + at.Replace(" A(", " B("))));
    }

    [Fact]
    public void Generated_excluded_editor_test_and_capture_inputs_are_skipped()
    {
        using var host = new AnalyzerTestHost();
        var methods = Method("A") + Method("B");
        Assert.Empty(host.AnalyzeDuplicates(Source("// <auto-generated />\n" + methods, path: "Assets/Generated.g.cs")));
        Assert.Empty(host.AnalyzeDuplicatesWithDefines(Source(methods).Source, "UNITY_EDITOR"));
        Assert.Empty(host.AnalyzeDuplicatesWithDefines(Source(methods).Source, "UNITY_INCLUDE_TESTS"));
        host.WriteSettings("""{"schemaVersion":1,"codeSize":{"includeGlobs":["Assets/Included/**/*.cs"]}}""");
        Assert.Empty(host.AnalyzeDuplicates(Source(methods)));
        host.WriteSettings("""{"schemaVersion":1,"codeSize":{"includeGlobs":["Assets/**/*.cs"]}}""");
        host.WriteCapture("""{"nonce":"capture","mode":"Initial"}""");
        Assert.Empty(host.AnalyzeDuplicates(Source(methods)));
    }

    [Fact]
    public void Size_baseline_does_not_suppress_duplicates()
    {
        using var host = new AnalyzerTestHost();
        host.WriteBaseline("""{"schemaVersion":1,"entries":[{"ruleId":"RYXPA1002","assemblyName":"Assembly-CSharp","symbolId":"M:C.A(System.Int32[])","allowedSloc":100}]}""");
        Assert.Single(host.AnalyzeDuplicates(Source(Method("A") + Method("B"))));
    }

    private static int CountOperations(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Count", new[] { tree }, references);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(method.Body!)!;
        return operation.DescendantsAndSelf().Count(o => !o.IsImplicit && o is not IBlockOperation);
    }
}
