using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.TestSupport;

/// <summary>
/// The image-conditions lane's test filters (plan 0.8), written once in <c>image-conditions-lane-filters.json</c> beside
/// this file: CI's lane and <c>eng/test-fast.ps1 -ImageConditions</c> run each test app with its filter there. A term that
/// names no test includes or excludes nothing, and says nothing (the lane's annex term once matched no test, and two of
/// its exclusions no class), so each test app holds every term of its own filter to a test it has.
/// </summary>
public static class ImageConditionsLane
{
    private static readonly Regex Term = new(@"FullyQualifiedName!?~([^|&()]+)", RegexOptions.CultureInvariant);

    /// <summary>The lane's filter for a test app: <c>lex_v3_tests</c> or <c>ingest_request_time</c>.</summary>
    public static string Filter(string testApp)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Checkout root not found above the test binaries.");
        using var filters = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory.FullName, "tests", "Lex.V3.TestSupport", "image-conditions-lane-filters.json")));
        return filters.RootElement.GetProperty(testApp).GetString()!;
    }

    /// <summary>
    /// The terms of <paramref name="filter"/> that are part of no test's fully qualified name in <paramref name="tests"/>,
    /// after holding the filter to the one shape these terms read: <c>FullyQualifiedName~</c> or <c>!~</c> a name,
    /// joined by <c>|</c>, <c>&amp;</c> and parentheses.
    /// </summary>
    public static IReadOnlyList<string> TermsNamingNoTest(string filter, Assembly tests)
    {
        Assert.AreEqual(
            string.Empty, Regex.Replace(Term.Replace(filter, string.Empty), @"[|&()]", string.Empty),
            $"Every term of the filter is FullyQualifiedName~ or !~ a name: {filter}");
        var names = tests.GetTypes()
            .Where(static type => type.GetCustomAttribute<TestClassAttribute>() is not null)
            .SelectMany(static type => type.GetMethods()
                .Where(static method => method.GetCustomAttribute<TestMethodAttribute>() is not null)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToArray();
        Assert.IsNotEmpty(names, "the test app's tests");
        return Term.Matches(filter)
            .Select(static match => match.Groups[1].Value)
            .Where(term => !names.Any(name => name.Contains(term, StringComparison.Ordinal)))
            .ToArray();
    }
}
