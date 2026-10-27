namespace Lex.V3.Ingest.Europe;

/// <summary>
/// One held article of an EU expression without its text, as a search lists the wordings it reads: the article identity, the
/// corpus member it was read from, the publisher's article id and heading, the Formex act date of the wording, the language,
/// and, where the index stores it (schema 4 on), the SHA-256 of the text's UTF-8 bytes, by which the articles a consolidated
/// version did not change are read and matched once.
/// </summary>
public sealed record EuropeIndexArticleRow(
    string ArticleIdentitySha256,
    string ObjectRefSha256,
    string PublisherIdentifier,
    string Heading,
    string WordingDate,
    string Language,
    string? TextSha256);

/// <summary>What an EU search reads of the index: an expression's articles without their text, and the text of the ones it matches in.</summary>
public sealed partial class EuropeIndexReader
{
    /// <summary>
    /// Every held article of one EU expression without its text, in the publisher's article id and article identity order (the
    /// order <see cref="ResolveExpressionArticles"/> lists them in, and the wording digest reads them in). Empty when the index
    /// holds no article of that expression; the text digest is null on an index schema that stores none.
    /// </summary>
    public IReadOnlyList<EuropeIndexArticleRow> ResolveExpressionArticleRows(string publisherExpressionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherExpressionId);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT a.article_identity_sha256,a.object_ref_sha256,a.publisher_identifier,a.heading,a.wording_date,a.language," +
                (HasArticleByteDigests ? "d.text_sha256 " : "NULL ") +
                "FROM articles AS a INDEXED BY articles_object_ref " +
                (HasArticleByteDigests ? "LEFT JOIN article_digests d USING(article_identity_sha256) " : "") +
                "WHERE " + ObjectsIn(command, "a.object_ref_sha256", _objects.OfExpression(publisherExpressionId)) +
                " AND a.publisher_expression_id=$expression ORDER BY a.publisher_identifier,a.article_identity_sha256";
            command.Parameters.AddWithValue("$expression", publisherExpressionId);
            using var reader = command.ExecuteReader();
            var rows = new List<EuropeIndexArticleRow>();
            while (reader.Read())
            {
                rows.Add(new EuropeIndexArticleRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return Array.AsReadOnly(rows.ToArray());
        }
    }

    /// <summary>
    /// The searchable text of each of the given article identities (the text <see cref="ResolveExpressionArticles"/> carries
    /// and evidence_bundle quotes), read by primary key; an identity the index does not hold is absent.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolveArticleTexts(IReadOnlyCollection<string> articleIdentities)
    {
        ArgumentNullException.ThrowIfNull(articleIdentities);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (articleIdentities.Count == 0)
        {
            return texts;
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT article_identity_sha256,searchable_text FROM articles " +
                "WHERE article_identity_sha256 IN (SELECT value FROM json_each($identities))";
            command.Parameters.AddWithValue("$identities", System.Text.Json.JsonSerializer.Serialize(articleIdentities.Distinct(StringComparer.Ordinal).ToArray()));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                texts[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return texts;
    }
}
