using Lex.V3.Api;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class SyntheticBootstrapDiagnosticTests
{
    [TestMethod]
    public void FailureReasonsAreClosedAndDoNotExposeExceptionText()
    {
        var cases = new (Exception Exception, string Reason)[]
        {
            (new SyntheticImmutableCustodyException("secret custody detail"), "immutable_custody"),
            (new FileNotFoundException("secret file path"), "required_file_missing"),
            (new UnauthorizedAccessException("secret access path"), "required_file_unreadable"),
            (new InvalidDataException("secret artifact member"), "invalid_artifact_or_index"),
            (new OperationCanceledException("secret cancellation detail"), "startup_cancelled"),
            (new Exception("secret unexpected detail"), "unexpected"),
        };

        foreach (var item in cases)
        {
            var diagnostic = SyntheticBootstrapDiagnostic.Describe(item.Exception);
            Assert.AreEqual(
                $"lex_v3_preview_bootstrap_failed reason={item.Reason}",
                diagnostic);
            Assert.IsFalse(diagnostic.Contains("secret", StringComparison.Ordinal));
            Assert.IsTrue(diagnostic.All(static character => character <= 0x7f));
        }
    }
}
