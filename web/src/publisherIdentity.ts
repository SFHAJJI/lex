/**
 * The one publisher-identity validator, shared by the envelope strip, the population footer and
 * the capability-limitation list.
 *
 * WHY ONE. Publisher identity is the join key across three independent disclosures: which index a
 * row came from, which denominator applies to it, and which limitation explains a gap. Three
 * validators that disagree about what counts as one publisher let the product show a denominator
 * belonging to one logical identity beside rows belonging to another. Two failure modes, pulling
 * in opposite directions:
 *
 *   ALIASING  two raw values that are not the same identity treated as one, by case folding or by
 *             trimming, so one publisher's rows are checked against another's denominator.
 *   SPLITTING one raw value becoming two logical identities in different modules, so a
 *             publisher's population is voided in one place and honoured in another.
 *
 * Before this module the workspace had both. The strip validated `publisher` through the same
 * `str` helper it used for commit hashes, which TRIMS, so " lu-legilux " became lu-legilux there
 * and was refused outright by the other two. The population and limitation regexes carried the
 * `i` flag, so "LU-Legilux" was an identity to them and not to the producer's ordinal lookup.
 *
 * NON-NORMALIZING, AND WHY THAT IS THE WHOLE POINT. This returns the raw value or nothing. It
 * never trims, never lower-cases and never repairs, because repairing is exactly how one raw
 * value becomes a DIFFERENT logical identity. A padded value is not a value to be cleaned; it is
 * evidence that something upstream is not the producer, and the honest answer is to refuse it.
 *
 * THE GRAMMAR, READ FROM THE PRODUCER, NOT FROM INTUITION
 *
 * Mint. `src/Lex.Ingest/SourceAdapterRegistry.cs` holds the publisher registry in a
 * `Dictionary<string, Registration>(StringComparer.Ordinal)` and registers exactly "lu-legilux"
 * and "eu-eurlex". `Resolve` throws `Unknown publisher` for any key that misses that ORDINAL
 * lookup, so the only publisher ids an ingest run can mint are registry keys, and they are
 * lower-case ASCII with a hyphen. `src/Lex.Ingest/Program.cs` reads `--publisher` and hands it
 * straight to `Resolve`, so there is no second door.
 *
 * Carriage. `src/Lex.Ingest/IndexFromCorpus.cs` writes `["collection"] = publisherId` into the
 * signed index stamp. `src/Lex.Index/IndexReader.cs` exposes
 * `Collection => Stamp.GetValueOrDefault("collection", "?")`. `src/Lex.Mcp/McpCore.cs` mints the
 * envelope field as `["publisher"] = r.Collection`, verbatim. Nothing reformats it in between, so
 * the value this client validates is the registry key that the ingest run resolved.
 *
 * Comparison. `McpCore.SelectReaders` selects with `reader.Collection == publisher`, an ORDINAL
 * string comparison. `UnmountedFilter` says so in as many words: "Publisher selection compares
 * ordinally". The case-insensitive step next to it is not a case-insensitive identity; it exists
 * only to RESTORE the mounted spelling into the arguments before that ordinal selection runs, and
 * the server then reports an unmatched value rather than guessing. Jurisdiction, by contrast, is
 * compared `OrdinalIgnoreCase` in the same function. The producer treats the two vocabularies
 * differently on purpose, which is why this module validates publisher only.
 *
 * Character class and bound. `src/Lex.Ask/UiEffects.cs` is the producer-side validator for the
 * assistant's `publisher_limitations` field:
 *     value is { Length: > 0 and <= 64 }
 *     && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
 * so: ASCII alphanumerics plus hyphen and underscore, at least one character, at most 64.
 * `src/Lex.Mcp/LegalOperationCatalog.cs` bounds the publisher argument by the same
 * `MaximumShortLength = 64`.
 *
 * WHERE THIS IS NARROWER THAN THAT C# PREDICATE, AND WHY. `char.IsAsciiLetterOrDigit` admits
 * A-Z. The MINT does not: registry identities are ordinal lower-case, and an uppercase spelling
 * of a registered publisher is a value the producer cannot emit. Admitting it here would give one
 * publisher two spellings that pass every duplicate check the other should have failed. That is
 * the single dimension narrowed, and it is narrowed towards the mint.
 *
 * Hyphen and underscore are NOT narrowed away. Underscore is unobserved in the two shipped ids,
 * but it is inside the producer's declared class and refusing it would narrow the grammar on a
 * guess rather than on evidence. Nothing about underscore creates an alias: it is not a case
 * variant or a padding variant of anything.
 *
 * The bound is 64, the producer's own. The strip previously bounded `publisher` at 128, the
 * length it uses for commit hashes and digests; that was a bound about hashes, not about
 * publishers, and it is not a bound the producer ever declared for this field.
 *
 * A note on "?". `IndexReader.Collection` falls back to "?" when a stamp carries no collection.
 * The population and limitation validators already refused that sentinel; the strip accepted it
 * and would render a row titled "?". Refusing it in all three is the fail-closed reading and
 * removes one more way for the three to disagree.
 */

/** The producer's own identifier bound: `UiEffects.Identifier` and `MaximumShortLength`. */
export const MAX_PUBLISHER_IDENTITY = 64;

/**
 * No `i` flag, deliberately. The flag is the alias. It is also invisible at the call site, which
 * is how it survived three review rounds in two separate modules.
 *
 * No anchoring subtlety either: JavaScript's `$` without `m` matches only at end of input, so
 * "lu-legilux\n" is refused by the character class rather than by luck.
 */
const PUBLISHER_IDENTITY = /^[a-z0-9_-]+$/;

/**
 * The raw value when it is an identity the producer could have minted, otherwise undefined.
 *
 * Total and fail-closed: a non-string, an absent value, an empty string, an overlong one, a
 * padded one and a case alias all yield undefined. The returned string is reference-equal to the
 * input, which is the property callers depend on when they key a Map by it.
 */
export function publisherIdentity(raw: unknown): string | undefined {
  return typeof raw === "string"
    && raw.length > 0
    && raw.length <= MAX_PUBLISHER_IDENTITY
    && PUBLISHER_IDENTITY.test(raw)
    ? raw
    : undefined;
}
