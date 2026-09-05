using System.Reflection;
using System.Text;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.TestSupport;

/// <summary>
/// Finds every <see cref="ICustodyStore"/> implementation in an assembly and drives the obligations
/// the interface states, so conformance is measured rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// Residue R2. The interface states two obligations on every store: a receipt exists only after the
/// store has read back the bytes and observed their protection, with a mismatch raising
/// <see cref="CustodyIntegrityException"/>; and after a receipt,
/// <see cref="ICustodyStore.ReadByDigestAsync"/> finds the object by its content address alone. Both
/// were written as contract and neither was checked across implementations.
/// </para>
/// <para>
/// Conforming by default. The sweep selects on the type implementing the interface and nothing
/// else, and any implementation with a public parameterless constructor is driven without anyone
/// opting it in. An implementation that cannot be built that way is either given a named recipe
/// here or declared exempt with a reason by the caller, and the caller asserts that driven plus
/// exempt equals swept. An exclusion list would be a sweep narrowed by its own expected answer,
/// which is the defect the closed surface census exists to remove; a partition is not, because a
/// new implementation belongs to neither half until somebody puts it in one.
/// </para>
/// <para>
/// WHAT THIS PROVES, IN EFFECT RATHER THAN MECHANISM. Obligation two is driven directly: after a
/// receipt, the digest alone returns the bytes. Obligation one is proven BY ITS EFFECT, because the
/// readback and the protection observation are internal to a store and a contract test binds
/// observable behaviour. Three effects are bound. The bytes are retrievable at the reference the
/// write returned. The receipt describes those bytes exactly. And the policy evidence describes the
/// SAME object, by digest and by byte length and by custody class, while the protection it declares
/// is reported to the caller and PINNED PER STORE PER LANE.
/// </para>
/// <para>
/// That pin is the only thing that actually compares a declared protection, and it replaces an
/// assertion that could not fail. An earlier version asked whether the protection was a defined enum
/// member. The CustodyPolicyEvidence constructor already refuses an undefined one, so no receipt
/// could ever reach that check carrying one, and the remark above it claimed the declared protection
/// was verified while nothing compared it to anything. That is an unfailable assertion carrying a
/// claim, which is the shape this census was built to find in production code, appearing a second
/// time in the instrument instead. THE INSTRUMENT IS NOT EXEMPT FROM THE PROPERTY IT MEASURES.
/// </para>
/// <para>
/// THE MISMATCH CLAUSE IS DRIVEN, and the reasoning that once said it could not be is recorded here
/// because the mistake is more useful than the fix. It ran: a conforming store never mismatches, so
/// only a fault double can drive the clause, and the fault doubles are exempt by construction. The
/// first half is true and the conclusion does not follow, because it assumes the fault has to be
/// injected AT THE IMPLEMENTATION. It can be injected AT THE STORAGE. A store that writes to a
/// filesystem touches a surface the test can reach, so the test writes through the store, alters
/// the stored bytes underneath it, and then asserts the store refuses them. The subject stays
/// conforming and the clause is still exercised. The general form, worth more than this one test:
/// A CEILING ON WHAT A TEST CAN OBSERVE IS ITSELF A CLAIM and deserves the same scrutiny as any
/// other. Before recording a limit, ask what OTHER surface the thing under test touches, because
/// that is usually where a fault can be injected without corrupting the subject.
/// </para>
/// </remarks>
public static class CustodyStoreConformance
{
    /// <summary>
    /// Every type in <paramref name="assemblies"/> that implements <see cref="ICustodyStore"/>,
    /// ordered by full name. Nested and non-public types are included, because a double is a store
    /// whoever can see it.
    /// </summary>
    /// <remarks>
    /// The one narrowing is that an interface extending <see cref="ICustodyStore"/> is not an
    /// implementation of it, and it is stated because an undocumented clause in a sweep is how nine
    /// abstract bases once sat outside a census. An abstract class IS swept: it implements the
    /// interface, it cannot be constructed, and so it has to be declared exempt with a reason rather
    /// than vanish.
    /// </remarks>
    public static IReadOnlyList<Type> ImplementationTypes(params string[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return assemblies
            .Select(static name => Assembly.Load(new AssemblyName(name)))
            .SelectMany(AllTypes)
            .Where(static type =>
                typeof(ICustodyStore).IsAssignableFrom(type)
                && type != typeof(ICustodyStore)
                && !type.IsInterface)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The same sweep rendered as full names, for a pin.</summary>
    public static IReadOnlyList<string> Implementations(params string[] assemblies) =>
        ImplementationTypes(assemblies).Select(static type => type.FullName!).ToArray();

    /// <summary>
    /// True when the harness can build one without being told how: a public parameterless
    /// constructor. This is the conforming-by-default rule, so a double written tomorrow is driven
    /// unless somebody declares why not.
    /// </summary>
    public static bool IsDrivenByDefault(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsAbstract)
        {
            return false;
        }

        return type.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null) is not null;
    }

    /// <summary>
    /// True when this file knows how to build one that has no parameterless constructor.
    /// </summary>
    /// <remarks>
    /// This is a table keyed by name, and that is worth naming honestly: it says HOW to build a
    /// store, never WHETHER to sweep one. A type absent from it is not skipped; it falls to the
    /// caller to drive or to declare exempt with a reason. Leaving the production store out because
    /// its constructor takes a root would have gutted the exercise, so it has a recipe.
    /// </remarks>
    public static bool HasRecipe(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return string.Equals(
            type.FullName, "Lex.V3.Artifacts.FileSystemCustodyStore", StringComparison.Ordinal);
    }

    /// <summary>Builds a store the harness knows how to build, or throws saying it cannot.</summary>
    /// <param name="scratchRoot">
    /// A directory the caller owns and deletes, built at run time and never a literal.
    /// </param>
    public static ICustodyStore Construct(Type type, string scratchRoot)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);

        if (HasRecipe(type))
        {
            var recipe = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                [typeof(string), typeof(TimeProvider)],
                modifiers: null)
                ?? throw new InvalidOperationException(
                    $"The recipe for {type.FullName} no longer matches its constructor.");
            return (ICustodyStore)recipe.Invoke([scratchRoot, TimeProvider.System]);
        }

        if (!IsDrivenByDefault(type))
        {
            throw new InvalidOperationException(
                $"{type.FullName} has no parameterless constructor and no recipe.");
        }

        return (ICustodyStore)Activator.CreateInstance(type, nonPublic: true)!;
    }

    /// <summary>
    /// Everything one write shows: whether the store refused the lane, whether an obligation went
    /// unmet, and what protection the policy evidence declared.
    /// </summary>
    /// <remarks>
    /// One write decides all three. An earlier version probed a lane with one write to learn whether
    /// it was accepted and then wrote again to learn how it refused, which cost two writes and could
    /// report a refusal kind of accepted on retry when the second write behaved differently from the
    /// first. A store is entitled to differ between two writes; a classification that depends on
    /// which write it read is not a classification.
    /// </remarks>
    public readonly record struct ObligationOutcome(
        string? Failure,
        string? Refusal,
        CustodyProtection? DeclaredProtection);

    /// <summary>
    /// Drives both obligations against one store with a single write. A failure is a finding, not
    /// something to be silenced, and a refusal is not a failure.
    /// </summary>
    public static async Task<ObligationOutcome> RunObligationsAsync(
        ICustodyStore store, CustodyClass custodyClass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var payload = Payload(store.GetType(), custodyClass);
        var digest = CustodyDigest.Of(payload.Span);

        DurableBlobWriteReceipt receipt;
        try
        {
            receipt = await store.CreateAsync(payload, custodyClass, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new(null, exception.GetType().Name, null);
        }

        if (receipt is null)
        {
            return new("CreateAsync returned no receipt", null, null);
        }

        var declared = receipt.PolicyEvidence?.Protection;
        var described = DescribesTheBytes(receipt, digest, payload.Length, custodyClass);
        if (described is not null)
        {
            return new(described, null, declared);
        }

        // Obligation one, in the only form observable from outside: a receipt implies the bytes are
        // retrievable at the reference the write returned.
        try
        {
            var atReference = await store.ReadAsync(receipt.Reference, cancellationToken)
                .ConfigureAwait(false);
            if (!atReference.Span.SequenceEqual(payload.Span))
            {
                return new(
                    "ReadAsync on the receipt reference returned different bytes", null, declared);
            }
        }
        catch (Exception exception)
        {
            return new(
                $"ReadAsync on the receipt reference threw {exception.GetType().Name}: "
                    + Trim(exception.Message),
                null,
                declared);
        }

        // Obligation two, which is a genuinely different property: the digest alone resolves it.
        try
        {
            var byDigest = await store.ReadByDigestAsync(digest, cancellationToken)
                .ConfigureAwait(false);
            if (!byDigest.Span.SequenceEqual(payload.Span))
            {
                return new(
                    "ReadByDigestAsync returned different bytes than were held", null, declared);
            }
        }
        catch (Exception exception)
        {
            return new(
                $"ReadByDigestAsync threw {exception.GetType().Name}: {Trim(exception.Message)}",
                null,
                declared);
        }

        return new(null, null, declared);
    }

    /// <summary>
    /// Drives the mismatch clause by injecting the fault AT THE STORAGE rather than at the
    /// implementation: write through the store, let <paramref name="alterStoredBytes"/> change what
    /// the store wrote, then require that the store refuses the altered bytes. Returns null when
    /// the store refuses correctly, or the failure otherwise.
    /// </summary>
    /// <remarks>
    /// Two refusals are required and they are different. The checked read must raise
    /// <see cref="CustodyIntegrityException"/>, which is the clause as written. And
    /// <see cref="ICustodyStore.ReadByDigestAsync"/> must not hand the altered bytes back as the
    /// object for that digest, whether it throws or returns nothing usable; returning them would
    /// mean a content address resolving to content it does not name, which is the failure the whole
    /// store design exists to prevent.
    /// </remarks>
    public static async Task<string?> RunStorageMismatchAsync(
        ICustodyStore store,
        Func<DurableBlobWriteReceipt, ReadOnlyMemory<byte>, Task<bool>> alterStoredBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(alterStoredBytes);

        var payload = Payload(store.GetType(), CustodyClass.NightlyFloor90d);
        var digest = CustodyDigest.Of(payload.Span);
        var receipt = await store
            .CreateAsync(payload, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);

        if (!await alterStoredBytes(receipt, payload).ConfigureAwait(false))
        {
            return "the stored bytes could not be altered, so the clause was never driven";
        }

        var raised = false;
        try
        {
            await CustodyRestore.ReadCheckedAsync(store, receipt.Reference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CustodyIntegrityException)
        {
            raised = true;
        }
        catch (Exception exception)
        {
            return "the checked read raised " + exception.GetType().Name
                + " rather than CustodyIntegrityException: " + Trim(exception.Message);
        }

        if (!raised)
        {
            return "the checked read returned altered bytes instead of raising "
                + "CustodyIntegrityException";
        }

        try
        {
            var byDigest = await store.ReadByDigestAsync(digest, cancellationToken)
                .ConfigureAwait(false);
            if (byDigest.Span.SequenceEqual(payload.Span))
            {
                return "ReadByDigestAsync returned the original bytes after the stored bytes were "
                    + "altered, so it did not read the storage the write used";
            }

            return "ReadByDigestAsync handed back the altered bytes as the object for that digest";
        }
        catch (CustodyIntegrityException)
        {
            return null;
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return "ReadByDigestAsync raised " + exception.GetType().Name
                + " rather than refusing the altered bytes as an integrity failure: "
                + Trim(exception.Message);
        }
    }

    /// <summary>
    /// The receipt has to describe the bytes that were presented, and its policy evidence has to be
    /// about the SAME object: same digest, same byte length, same custody class. What protection it
    /// declares is reported rather than judged here, because the only honest comparison for that is
    /// a literal the caller pins per store per lane.
    /// </summary>
    private static string? DescribesTheBytes(
        DurableBlobWriteReceipt receipt, string digest, int length, CustodyClass custodyClass)
    {
        if (!string.Equals(receipt.Reference.ContentSha256, digest, StringComparison.Ordinal))
        {
            return "the receipt names a different content digest than the bytes presented";
        }

        if (receipt.Reference.ByteLength != length)
        {
            return $"the receipt declares {receipt.Reference.ByteLength} bytes, not {length}";
        }

        if (receipt.Reference.CustodyClass != custodyClass)
        {
            return $"the receipt holds the bytes under {receipt.Reference.CustodyClass}, "
                + $"not the requested {custodyClass}";
        }

        if (receipt.PolicyEvidence is null)
        {
            return "the receipt carries no policy evidence";
        }

        if (!string.Equals(
                receipt.PolicyEvidence.Reference.ContentSha256, digest, StringComparison.Ordinal))
        {
            return "the policy evidence describes a different object than the receipt";
        }

        if (receipt.PolicyEvidence.Reference.ByteLength != length)
        {
            return $"the policy evidence declares {receipt.PolicyEvidence.Reference.ByteLength} "
                + $"bytes, not the {length} the receipt holds";
        }

        if (receipt.PolicyEvidence.Reference.CustodyClass != custodyClass)
        {
            return "the policy evidence names custody class "
                + $"{receipt.PolicyEvidence.Reference.CustodyClass}, not the requested {custodyClass}";
        }

        return null;
    }

    /// <summary>
    /// Bytes unique to the store type and custody class, so one store cannot pass on another's
    /// object and a failure names bytes a reader can reproduce.
    /// </summary>
    private static ReadOnlyMemory<byte> Payload(Type type, CustodyClass custodyClass) =>
        Encoding.UTF8.GetBytes($"custody conformance R2\n{type.FullName}\n{custodyClass}\n");

    private static string Trim(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length <= 120 ? line : line[..120];
    }

    private static IEnumerable<Type> AllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // A type that will not load is a type this cannot drive, and dropping it silently would
            // shrink the sweep without saying so. The partition over the whole list is what fails.
            return exception.Types.Where(static type => type is not null).Select(static type => type!);
        }
    }
}

/// <summary>
/// Runs <see cref="CustodyStoreConformance"/> over a scope, so both test projects drive their
/// implementations the same way rather than two ways that drift.
/// </summary>
/// <remarks>
/// <para>
/// One custody lane is required and the other is not, and the reason is the contract rather than
/// convenience. The obligations bind a write THAT PRODUCES A RECEIPT: they say what must already be
/// true when one exists, not that one must exist for every lane a caller can name. A store that
/// cannot observe the protection a lane demands is right to issue no receipt, and
/// <see cref="CustodyPolicyEvidence"/> enforces exactly that by refusing to describe a lane its
/// observed protection does not satisfy.
/// </para>
/// <para>
/// So <see cref="CustodyClass.NightlyFloor90d"/> is required: it is the ordinary transport lane and
/// a store that serves nothing at all would otherwise pass by refusing everything. Every other lane
/// is attempted, and a refusal is recorded rather than failed. The recorded refusals are pinned by
/// the caller, so "this store declines that lane" stays a stated fact that a change makes visible,
/// instead of a silent tolerance that would let a store quietly stop serving a lane it once served.
/// </para>
/// <para>
/// An earlier version of this file required every lane of every store and reported seven in-memory
/// doubles as non conforming. They were not. That was the harness claiming more than the contract
/// says, which is the same defect in a test that the census exists to remove from production code.
/// </para>
/// </remarks>
public static class ConformanceRun
{
    /// <summary>The lane every store must serve for its obligations to be observable at all.</summary>
    public const CustodyClass RequiredLane = CustodyClass.NightlyFloor90d;

    /// <summary>What one sweep of a scope found.</summary>
    /// <param name="Failures">
    /// Obligations not met, as <c>full name under lane: failure</c>. Empty when every driven store
    /// conformed on every lane it accepted.
    /// </param>
    /// <param name="DeclinedLanes">
    /// Lanes a store refused to write, as <c>full name declines lane: exception type</c>. A refusal
    /// is not a failure; it is a store declining to issue evidence it cannot back.
    /// </param>
    /// <param name="Declarations">
    /// What each accepted write declared, as <c>full name under lane: protection</c>. The caller
    /// pins this, which is the only comparison that makes a declared protection a checked fact
    /// rather than a value nothing ever reads.
    /// </param>
    public readonly record struct ConformanceOutcome(
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> DeclinedLanes,
        IReadOnlyList<string> Declarations);

    public static async Task<ConformanceOutcome> RunAsync(
        string[] scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var failures = new List<string>();
        var declined = new List<string>();
        var declarations = new List<string>();

        foreach (var type in CustodyStoreConformance.ImplementationTypes(scope))
        {
            if (!CustodyStoreConformance.IsDrivenByDefault(type)
                && !CustodyStoreConformance.HasRecipe(type))
            {
                continue;
            }

            var scratch = NewScratch();
            try
            {
                var store = CustodyStoreConformance.Construct(type, scratch);
                foreach (var lane in Enum.GetValues<CustodyClass>())
                {
                    // One write per lane, classified once from its own result.
                    var outcome = await CustodyStoreConformance
                        .RunObligationsAsync(store, lane, cancellationToken)
                        .ConfigureAwait(false);

                    if (outcome.Refusal is not null)
                    {
                        if (lane == RequiredLane)
                        {
                            failures.Add($"{type.FullName} under {lane}: refused the required lane "
                                + $"with {outcome.Refusal}");
                        }
                        else
                        {
                            declined.Add($"{type.FullName} declines {lane}: {outcome.Refusal}");
                        }

                        continue;
                    }

                    if (outcome.DeclaredProtection is not null)
                    {
                        declarations.Add(
                            $"{type.FullName} under {lane}: {outcome.DeclaredProtection}");
                    }

                    if (outcome.Failure is not null)
                    {
                        failures.Add($"{type.FullName} under {lane}: {outcome.Failure}");
                    }
                }
            }
            finally
            {
                Delete(scratch);
            }
        }

        failures.Sort(StringComparer.Ordinal);
        declined.Sort(StringComparer.Ordinal);
        declarations.Sort(StringComparer.Ordinal);
        return new(failures, declined, declarations);
    }

    /// <summary>A fresh directory this run owns, built at run time and never a literal.</summary>
    public static string NewScratch()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "lex-custody-conformance", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string scratch)
    {
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
            // A store may still hold a handle. The directory is under the system temporary root and
            // is not evidence, so a failure to remove it is not a test failure.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        // Remove the shared parent too, but only while it is empty, so a run leaves nothing behind
        // and a concurrent run keeps its own. Non recursive delete is what makes that safe: it
        // fails rather than reaching into another run.
        try
        {
            Directory.Delete(Path.GetDirectoryName(scratch)!);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
