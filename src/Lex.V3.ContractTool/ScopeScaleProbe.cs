using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.ContractTool;

internal static class ScopeScaleProbe
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ObservedSequenceDomain =
        "lex-v3-source-scope-observed-object-sequence/1\n";
    private const string FixtureIdentity = "lex-v3-source-scope-scale-fixture/1";

    public static string Run(int objectCount, string outputDirectory)
    {
        if (objectCount is <= 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectCount),
                "The scale probe accepts 1 through 1,000,000 objects.");
        }

        var outputRoot = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputRoot);

        var fixture = new SyntheticFixture();
        var sortKeys = new ScopeSortKey[objectCount];
        for (var generatorIndex = 0; generatorIndex < sortKeys.Length; generatorIndex++)
        {
            sortKeys[generatorIndex] = ScopeSortKey.Create(
                ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                    fixture.CreateObject(generatorIndex)),
                generatorIndex);
        }

        Array.Sort(sortKeys);
        for (var index = 1; index < sortKeys.Length; index++)
        {
            if (sortKeys[index - 1].HasSameDigest(sortKeys[index]))
            {
                throw new InvalidOperationException(
                    "The synthetic fixture produced a duplicate object digest.");
            }
        }

        var observedSequenceSha256 = ComputeObservedSequenceSha256(sortKeys);
        var resolver = new SyntheticResolver(fixture, objectCount, observedSequenceSha256);
        var snapshotPassCount = 0;
        IEnumerable<ScopeObjectReductionInput> OpenPass(CancellationToken cancellationToken)
        {
            snapshotPassCount++;
            foreach (var key in sortKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return fixture.CreateInput(key.GeneratorIndex);
            }
        }

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var stopwatch = Stopwatch.StartNew();
        var writerReceipt = ScopeManifestCanonicalWriter.WriteStreaming(
            Stream.Null,
            fixture.Profile,
            fixture.EvidenceArtifacts,
            objectCount,
            OpenPass,
            resolver);
        stopwatch.Stop();
        process.Refresh();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var memoryInfo = GC.GetGCMemoryInfo();

        if (snapshotPassCount != 2 || writerReceipt.ObjectCount != objectCount)
        {
            throw new InvalidOperationException(
                "The scale writer did not consume exactly two complete snapshot passes.");
        }

        var receipt = new
        {
            Schema = "lex-v3-source-scope-scale-receipt/1",
            Fixture = FixtureIdentity,
            FixtureIdentitySha256 = Sha256(FixtureIdentity + "\n"),
            FixtureKind = "synthetic_resolver_only",
            ObjectCount = objectCount,
            SnapshotPassCount = snapshotPassCount,
            ManifestSchema = writerReceipt.Schema,
            writerReceipt.ManifestSha256,
            writerReceipt.InputSequenceSha256,
            writerReceipt.CanonicalByteCount,
            WriterInvocationElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            WorkingSetBeforeWriterBytes = workingSetBefore,
            ProcessLifetimePeakWorkingSetBytes = process.PeakWorkingSet64,
            WriterInvocationAllocatedBytes = allocatedAfter - allocatedBefore,
            LastGcHeapSizeBytes = memoryInfo.HeapSizeBytes,
            LastGcFragmentedBytes = memoryInfo.FragmentedBytes,
            WriterInvocationGen0Collections = GC.CollectionCount(0) - gen0Before,
            WriterInvocationGen1Collections = GC.CollectionCount(1) - gen1Before,
            WriterInvocationGen2Collections = GC.CollectionCount(2) - gen2Before,
            SortKeyStorageBytes = (long)objectCount * Unsafe.SizeOf<ScopeSortKey>(),
            FirstPassProjectionStorageBytes = (long)objectCount * 5,
            ContractToolAssemblySha256 = FileSha256(Assembly.GetExecutingAssembly().Location),
            ContractsAssemblySha256 = FileSha256(
                typeof(ScopeManifestCanonicalWriter).Assembly.Location),
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CanonicalOutputPersisted = false,
            OutputSink = "hash_and_count_only",
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            receipt,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            });
        var receiptPath = Path.Combine(outputRoot, "source-scope-scale-receipt.json");
        File.WriteAllBytes(receiptPath, bytes);
        return receiptPath;
    }

    private static string ComputeObservedSequenceSha256(IReadOnlyList<ScopeSortKey> keys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(ObservedSequenceDomain));
        Span<byte> entry = stackalloc byte[4 + 32];
        for (var ordinal = 0; ordinal < keys.Count; ordinal++)
        {
            BinaryPrimitives.WriteInt32BigEndian(entry, ordinal);
            keys[ordinal].WriteDigest(entry[4..]);
            hash.AppendData(entry);
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class SyntheticFixture
    {
        private readonly SourceArtifactRef _enumerationRef =
            Artifact("33333333-3333-4333-8333-333333333333");
        private readonly SourceArtifactRef _profileRef =
            Artifact("c0e28bb7-f26a-4ea0-9628-d084fd3aaf22");
        private readonly SourceArtifactRef _tableRef =
            Artifact("ddaa3f1b-994d-47b8-83c7-e6221a90c388");
        private readonly SourceArtifactRef _objectRegistryRef =
            Artifact("44aa505f-d55f-4d6c-aef0-21ddcb46633d");
        private readonly SourceArtifactRef _identityProfileRef =
            Artifact("08ca1acc-142a-4807-8cc0-d84e412e1d07");
        private readonly ScopeSelectorEvidence[] _selectors;
        private readonly ScopeRuleEvaluation[] _evaluations;

        public SyntheticFixture()
        {
            var members = new[]
                {
                    Member(_profileRef, "body_candidate"),
                    Member(_tableRef, "body_allow"),
                    Member(_tableRef, "format"),
                    Member(_tableRef, "record_allow"),
                    Member(_tableRef, "relation_allow"),
                    Member(_tableRef, "support_allow"),
                }
                .OrderBy(static member => member.RegistryRef.ResourceId, StringComparer.Ordinal)
                .ThenBy(static member => member.RegistryRef.Sha256, StringComparer.Ordinal)
                .ThenBy(static member => member.MemberKey, StringComparer.Ordinal)
                .ToArray();
            int Ordinal(SourceArtifactRef registry, string key) => Array.FindIndex(
                members,
                member => member.RegistryRef == registry && member.MemberKey == key);

            Profile = new ScopeProfileBinding(
                _profileRef,
                _tableRef,
                members,
                [Ordinal(_tableRef, "format")],
                [
                    new ScopeRuleBinding(
                        ScopeAxis.Record,
                        Ordinal(_tableRef, "record_allow"),
                        0),
                    new ScopeRuleBinding(
                        ScopeAxis.Body,
                        Ordinal(_tableRef, "body_allow"),
                        1),
                    new ScopeRuleBinding(
                        ScopeAxis.Relation,
                        Ordinal(_tableRef, "relation_allow"),
                        2),
                    new ScopeRuleBinding(
                        ScopeAxis.SupportingDocument,
                        Ordinal(_tableRef, "support_allow"),
                        3),
                ],
                Ordinal(_profileRef, "body_candidate"));
            EvidenceArtifacts = [Artifact("11111111-1111-4111-8111-111111111111")];
            _selectors =
            [
                new ScopeSelectorEvidence(
                    ScopeSelectorState.PublisherValuePresent,
                    ["synthetic"],
                    ScopeSelectorEvidenceKind.ObservedValueSet,
                    0,
                    null,
                    null),
            ];
            _evaluations =
            [
                Matched(0, []),
                Matched(1, [Profile.BodyCandidateRoleMemberOrdinal]),
                Matched(2, []),
                Matched(3, []),
            ];
        }

        public ScopeProfileBinding Profile { get; }

        public IReadOnlyList<SourceArtifactRef> EvidenceArtifacts { get; }

        public SourceArtifactRef EnumerationRef => _enumerationRef;

        public ScopeSelectorEvidence Selector => _selectors[0];

        public ScopeRuleEvaluation Evaluation(int ordinal) => _evaluations[ordinal];

        public SourceObjectRef CreateObject(int generatorIndex)
        {
            var key = generatorIndex.ToString("D6");
            var canonicalKey = $"cellar:work:{key}";
            return new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Cellar,
                Member(_objectRegistryRef, "work"),
                $"http://publications.europa.eu/resource/cellar/{key}",
                canonicalKey,
                Sha256(canonicalKey),
                _identityProfileRef,
                null);
        }

        public ScopeObjectReductionInput CreateInput(int generatorIndex) => new(
            CreateObject(generatorIndex),
            _selectors,
            _evaluations);

        private static ScopeRuleEvaluation Matched(
            int ruleOrdinal,
            IReadOnlyList<int> roles) => new(
            ruleOrdinal,
            ScopeRuleEvaluationState.Matched,
            ScopeRuleEffect.Positive,
            ScopeDisposition.AcceptedSelected,
            roles,
            []);
    }

    private sealed class SyntheticResolver : IScopeReductionEvidenceResolver
    {
        private readonly SyntheticFixture _fixture;
        private readonly ScopeCompleteEnumerationBinding _enumeration;
        private string? _selectorEvidenceSha256;
        private string? _selectorSetSha256;
        private readonly string?[] _ruleEvaluationSha256;

        public SyntheticResolver(
            SyntheticFixture fixture,
            int objectCount,
            string observedSequenceSha256)
        {
            _fixture = fixture;
            _enumeration = new ScopeCompleteEnumerationBinding(
                fixture.EnumerationRef,
                fixture.Profile.SourceProfileRef,
                fixture.Profile.SelectorTableRef,
                objectCount,
                observedSequenceSha256);
            _ruleEvaluationSha256 = new string?[fixture.Profile.OrderedRules.Count];
        }

        public SourceArtifactRef CompleteEnumerationRef => _fixture.EnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding)
        {
            var profile = _fixture.Profile;
            var selectorMember = profile.OrderedMembers[
                profile.OrderedSelectorMemberOrdinals[0]];
            return binding.EvidenceKind == ScopeSelectorEvidenceKind.ObservedValueSet &&
                IsSha256(binding.ObjectRefSha256) &&
                binding.SelectorOrdinal == 0 &&
                binding.SelectorMember == selectorMember &&
                binding.SourceProfileRef == profile.SourceProfileRef &&
                binding.SelectorTableRef == profile.SelectorTableRef &&
                binding.EvidenceArtifactRef == _fixture.EvidenceArtifacts[0] &&
                Stable(ref _selectorEvidenceSha256, binding.SelectorEvidenceSha256);
        }

        public bool IsSelectorNotApplicableAdmitted(
            ScopeSelectorNotApplicableBinding binding) => false;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding)
        {
            var profile = _fixture.Profile;
            if (!IsSha256(binding.ObjectRefSha256) ||
                !IsSha256(binding.SelectorSetSha256) ||
                binding.RuleOrdinal < 0 ||
                binding.RuleOrdinal >= profile.OrderedRules.Count)
            {
                return false;
            }

            var rule = profile.OrderedRules[binding.RuleOrdinal];
            return binding.RuleMember == profile.OrderedMembers[rule.RuleMemberOrdinal] &&
                binding.SourceProfileRef == profile.SourceProfileRef &&
                binding.SelectorTableRef == profile.SelectorTableRef &&
                Stable(ref _selectorSetSha256, binding.SelectorSetSha256) &&
                Stable(
                    ref _ruleEvaluationSha256[binding.RuleOrdinal],
                    binding.RuleEvaluationSha256);
        }

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding == _enumeration;

        private static bool Stable(ref string? expected, string candidate)
        {
            if (!IsSha256(candidate))
            {
                return false;
            }

            expected ??= candidate;
            return string.Equals(expected, candidate, StringComparison.Ordinal);
        }

        private static bool IsSha256(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private readonly struct ScopeSortKey : IComparable<ScopeSortKey>
    {
        private ScopeSortKey(
            ulong first,
            ulong second,
            ulong third,
            ulong fourth,
            int generatorIndex)
        {
            First = first;
            Second = second;
            Third = third;
            Fourth = fourth;
            GeneratorIndex = generatorIndex;
        }

        private ulong First { get; }

        private ulong Second { get; }

        private ulong Third { get; }

        private ulong Fourth { get; }

        public int GeneratorIndex { get; }

        public static ScopeSortKey Create(string digest, int generatorIndex)
        {
            var bytes = Convert.FromHexString(digest);
            if (bytes.Length != 32)
            {
                throw new InvalidOperationException("The synthetic object digest is not SHA-256.");
            }

            return new ScopeSortKey(
                BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]),
                generatorIndex);
        }

        public int CompareTo(ScopeSortKey other)
        {
            var comparison = First.CompareTo(other.First);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = Second.CompareTo(other.Second);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = Third.CompareTo(other.Third);
            return comparison != 0 ? comparison : Fourth.CompareTo(other.Fourth);
        }

        public bool HasSameDigest(ScopeSortKey other) => CompareTo(other) == 0;

        public void WriteDigest(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, First);
            BinaryPrimitives.WriteUInt64BigEndian(destination[8..], Second);
            BinaryPrimitives.WriteUInt64BigEndian(destination[16..], Third);
            BinaryPrimitives.WriteUInt64BigEndian(destination[24..], Fourth);
        }
    }

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static SourceRegistryMemberRef Member(SourceArtifactRef registry, string key) =>
        new(registry, key);
}
