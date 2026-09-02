using System.Buffers;
using System.Text;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// The result of parsing and evaluating one robots policy for one request target.
/// </summary>
public enum RobotsPolicyEvaluationResult
{
    Allowed = 1,
    Denied = 2,
    UnsafeToInterpret = 3,
}

/// <summary>
/// Parses and evaluates a bounded RFC 9309 robots.txt observation.
/// Invalid recognized directives return <see cref="RobotsPolicyEvaluationResult.UnsafeToInterpret"/>.
/// Source: https://www.rfc-editor.org/rfc/rfc9309.html#section-2.2
/// </summary>
public static class RobotsExclusionPolicy
{
    internal const int MaximumPolicyBytes = 500 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static RobotsPolicyEvaluationResult Evaluate(
        ReadOnlySpan<byte> policyBytes,
        string productToken,
        string pathAndQuery)
    {
        ValidateProductToken(productToken);
        var normalizedPath = NormalizePath(pathAndQuery);
        if (!TryParse(policyBytes, out var groups))
        {
            return RobotsPolicyEvaluationResult.UnsafeToInterpret;
        }

        if (string.Equals(normalizedPath, "/robots.txt", StringComparison.Ordinal))
        {
            return RobotsPolicyEvaluationResult.Allowed;
        }

        var exactGroups = groups
            .Where(group => group.UserAgents.Any(agent =>
                string.Equals(agent, productToken, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var applicableGroups = exactGroups.Length > 0
            ? exactGroups
            : groups.Where(group => group.UserAgents.Contains("*", StringComparer.Ordinal)).ToArray();

        Rule? winner = null;
        foreach (var rule in applicableGroups.SelectMany(static group => group.Rules))
        {
            if (!Matches(normalizedPath, rule.Pattern, rule.RequiresEnd) ||
                (winner is not null && rule.Specificity < winner.Value.Specificity))
            {
                continue;
            }

            if (winner is null ||
                rule.Specificity > winner.Value.Specificity ||
                rule.IsAllow)
            {
                winner = rule;
            }
        }

        return winner is { IsAllow: false }
            ? RobotsPolicyEvaluationResult.Denied
            : RobotsPolicyEvaluationResult.Allowed;
    }

    private static bool TryParse(
        ReadOnlySpan<byte> policyBytes,
        out IReadOnlyList<Group> groups)
    {
        if (policyBytes.Length > MaximumPolicyBytes)
        {
            throw new ArgumentException(
                $"Robots policy bytes must not exceed {MaximumPolicyBytes} bytes.",
                nameof(policyBytes));
        }

        string policy;
        try
        {
            policy = StrictUtf8.GetString(policyBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException(
                "Robots policy bytes must be strict UTF-8.",
                nameof(policyBytes),
                exception);
        }

        ValidatePolicyCharacters(policy, nameof(policyBytes));
        if (policy.Length > 0 && policy[0] == '\uFEFF')
        {
            policy = policy[1..];
        }

        var parsedGroups = new List<Group>();
        Group? current = null;
        foreach (var sourceLine in policy.Split(['\r', '\n']))
        {
            var comment = sourceLine.IndexOf('#', StringComparison.Ordinal);
            var line = (comment >= 0 ? sourceLine[..comment] : sourceLine).Trim(' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim(' ', '\t');
            var value = line[(separator + 1)..].Trim(' ', '\t');
            if (string.Equals(key, "user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsPolicyUserAgent(value))
                {
                    groups = [];
                    return false;
                }

                if (current is null || current.HasRuleDirective)
                {
                    current = new Group();
                    parsedGroups.Add(current);
                }

                current.UserAgents.Add(value);
                continue;
            }

            var isAllow = string.Equals(key, "allow", StringComparison.OrdinalIgnoreCase);
            if (!isAllow && !string.Equals(key, "disallow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryCreateRule(value, isAllow, out var rule))
            {
                groups = [];
                return false;
            }

            if (current is null)
            {
                continue;
            }

            current.HasRuleDirective = true;
            if (rule is not null)
            {
                current.Rules.Add(rule.Value);
            }
        }

        groups = parsedGroups;
        return true;
    }

    private static bool TryCreateRule(string value, bool isAllow, out Rule? rule)
    {
        rule = null;
        if (value.Length == 0)
        {
            return true;
        }

        if (value[0] is not ('/' or '*'))
        {
            return false;
        }

        var requiresEnd = value[^1] == '$';
        var patternSource = requiresEnd ? value[..^1] : value;
        if (!TryNormalizePattern(patternSource, out var pattern))
        {
            return false;
        }

        rule = new Rule(
            isAllow,
            pattern,
            requiresEnd,
            pattern.Length + (requiresEnd ? 1 : 0));
        return true;
    }

    private static bool TryNormalizePattern(string value, out string normalized)
    {
        var result = new StringBuilder(value.Length);
        Span<byte> encoded = stackalloc byte[4];
        for (var index = 0; index < value.Length;)
        {
            var character = value[index];
            if (character == '*')
            {
                result.Append(character);
                index++;
                continue;
            }

            if (character == '%')
            {
                if (!TryReadPercentEscape(value, index, out var octet))
                {
                    normalized = string.Empty;
                    return false;
                }

                AppendNormalizedEscape(result, octet);
                index += 3;
                continue;
            }

            if (character <= 0x7f)
            {
                if (IsForbiddenPatternCharacter(character))
                {
                    normalized = string.Empty;
                    return false;
                }

                if (character == '$' || !IsPathAndQueryCharacter(character))
                {
                    AppendPercentEncoded(result, (byte)character);
                }
                else
                {
                    result.Append(character);
                }

                index++;
                continue;
            }

            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                normalized = string.Empty;
                return false;
            }

            var length = rune.EncodeToUtf8(encoded);
            foreach (var octet in encoded[..length])
            {
                AppendPercentEncoded(result, octet);
            }

            index += consumed;
        }

        normalized = result.ToString();
        return true;
    }

    private static string NormalizePath(string pathAndQuery)
    {
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        if (pathAndQuery.Length == 0 || pathAndQuery[0] != '/')
        {
            throw new ArgumentException(
                "A robots path-and-query must begin with '/'.",
                nameof(pathAndQuery));
        }

        var result = new StringBuilder(pathAndQuery.Length);
        Span<byte> encoded = stackalloc byte[4];
        for (var index = 0; index < pathAndQuery.Length;)
        {
            var character = pathAndQuery[index];
            if (character == '%')
            {
                if (!TryReadPercentEscape(pathAndQuery, index, out var octet))
                {
                    throw new ArgumentException(
                        "A robots path-and-query must contain only complete percent escapes.",
                        nameof(pathAndQuery));
                }

                AppendNormalizedEscape(result, octet);
                index += 3;
                continue;
            }

            if (character <= 0x7f)
            {
                if (!IsPathAndQueryCharacter(character))
                {
                    throw new ArgumentException(
                        "A robots path-and-query must contain only RFC 3986 path and query characters.",
                        nameof(pathAndQuery));
                }

                if (character is '*' or '$')
                {
                    AppendPercentEncoded(result, (byte)character);
                }
                else
                {
                    result.Append(character);
                }

                index++;
                continue;
            }

            var status = Rune.DecodeFromUtf16(
                pathAndQuery.AsSpan(index),
                out var rune,
                out var consumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "A robots path-and-query must contain only valid Unicode scalar values.",
                    nameof(pathAndQuery));
            }

            var length = rune.EncodeToUtf8(encoded);
            foreach (var octet in encoded[..length])
            {
                AppendPercentEncoded(result, octet);
            }

            index += consumed;
        }

        return result.ToString();
    }

    private static bool Matches(string path, string pattern, bool requiresEnd)
    {
        var pathIndex = 0;
        var patternIndex = 0;
        var isFirstSegment = true;

        while (true)
        {
            var wildcardIndex = pattern.IndexOf('*', patternIndex);
            var isFinalSegment = wildcardIndex < 0;
            var segmentEnd = isFinalSegment ? pattern.Length : wildcardIndex;
            var segment = pattern.AsSpan(patternIndex, segmentEnd - patternIndex);

            if (isFirstSegment)
            {
                if (!path.AsSpan().StartsWith(segment, StringComparison.Ordinal))
                {
                    return false;
                }

                pathIndex = segment.Length;
                isFirstSegment = false;
            }
            else if (isFinalSegment && requiresEnd)
            {
                var candidateIndex = path.Length - segment.Length;
                if (candidateIndex < pathIndex ||
                    !path.AsSpan(candidateIndex).SequenceEqual(segment))
                {
                    return false;
                }

                pathIndex = path.Length;
            }
            else
            {
                var relativeIndex = IndexOf(path.AsSpan(pathIndex), segment);
                if (relativeIndex < 0)
                {
                    return false;
                }

                pathIndex += relativeIndex + segment.Length;
            }

            if (isFinalSegment)
            {
                return !requiresEnd || pathIndex == path.Length;
            }

            patternIndex = wildcardIndex + 1;
        }
    }

    private static int IndexOf(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return 0;
        }

        var rented = ArrayPool<int>.Shared.Rent(value.Length);
        var prefix = rented.AsSpan(0, value.Length);

        try
        {
            prefix[0] = 0;
            for (var index = 1; index < value.Length; index++)
            {
                var matched = prefix[index - 1];
                while (matched > 0 && value[index] != value[matched])
                {
                    matched = prefix[matched - 1];
                }

                if (value[index] == value[matched])
                {
                    matched++;
                }

                prefix[index] = matched;
            }

            var sourceMatched = 0;
            for (var index = 0; index < source.Length; index++)
            {
                while (sourceMatched > 0 && source[index] != value[sourceMatched])
                {
                    sourceMatched = prefix[sourceMatched - 1];
                }

                if (source[index] == value[sourceMatched])
                {
                    sourceMatched++;
                }

                if (sourceMatched == value.Length)
                {
                    return index - value.Length + 1;
                }
            }

            return -1;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    private static void ValidateProductToken(string productToken)
    {
        ArgumentNullException.ThrowIfNull(productToken);
        if (productToken.Length == 0 || !productToken.All(static character =>
                character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '-' or '_'))
        {
            throw new ArgumentException(
                "A robots product token must contain only ASCII letters, '-' or '_'.",
                nameof(productToken));
        }
    }

    private static bool IsPolicyUserAgent(string value) =>
        string.Equals(value, "*", StringComparison.Ordinal) ||
        (value.Length > 0 && value.All(static character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '-' or '_'));

    private static void ValidatePolicyCharacters(string policy, string parameterName)
    {
        if (policy.Any(static character =>
                character is < '\u0020' and not ('\t' or '\r' or '\n') or '\u007f'))
        {
            throw new ArgumentException(
                "Robots policy bytes contain an ASCII control character outside RFC 9309.",
                parameterName);
        }
    }

    private static bool IsPathAndQueryCharacter(char value) =>
        IsUnreserved((byte)value) ||
        value is '/' or '?' or ':' or '@' or '!' or '$' or '&' or '\'' or
            '(' or ')' or '*' or '+' or ',' or ';' or '=';

    private static bool IsForbiddenPatternCharacter(char value) =>
        value is <= '\u0020' or '\u007f' or '#';

    private static bool TryReadPercentEscape(string value, int index, out byte octet)
    {
        octet = 0;
        if (index + 2 >= value.Length ||
            !TryHex(value[index + 1], out var high) ||
            !TryHex(value[index + 2], out var low))
        {
            return false;
        }

        octet = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryHex(char value, out int result)
    {
        result = value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'A' and <= 'F' => value - 'A' + 10,
            >= 'a' and <= 'f' => value - 'a' + 10,
            _ => -1,
        };
        return result >= 0;
    }

    private static void AppendNormalizedEscape(StringBuilder result, byte octet)
    {
        if (IsUnreserved(octet))
        {
            result.Append((char)octet);
        }
        else
        {
            AppendPercentEncoded(result, octet);
        }
    }

    private static bool IsUnreserved(byte value) =>
        value is (>= (byte)'A' and <= (byte)'Z') or
            (>= (byte)'a' and <= (byte)'z') or
            (>= (byte)'0' and <= (byte)'9') or
            (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';

    private static void AppendPercentEncoded(StringBuilder result, byte value)
    {
        const string hexadecimal = "0123456789ABCDEF";
        result.Append('%');
        result.Append(hexadecimal[value >> 4]);
        result.Append(hexadecimal[value & 0x0f]);
    }

    private sealed class Group
    {
        public List<string> UserAgents { get; } = [];

        public List<Rule> Rules { get; } = [];

        public bool HasRuleDirective { get; set; }
    }

    private readonly record struct Rule(
        bool IsAllow,
        string Pattern,
        bool RequiresEnd,
        int Specificity);
}
