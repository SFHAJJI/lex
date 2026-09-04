using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Scope;

public static class ScopeReducer
{
    public static VerifiedScopeManifest Reduce(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> orderedEvidenceArtifacts,
        IReadOnlyList<SourceObjectRef> observedObjects,
        IReadOnlyList<ScopeObjectReductionInput> inputs,
        IScopeReductionEvidenceResolver observationResolver)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(orderedEvidenceArtifacts);
        ArgumentNullException.ThrowIfNull(observedObjects);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(observationResolver);

        var evidenceArtifacts = ScopeValidation.CopySortedArtifacts(
            orderedEvidenceArtifacts,
            nameof(orderedEvidenceArtifacts));
        var observedEntries = observedObjects
            .Select(static objectRef =>
            {
                ArgumentNullException.ThrowIfNull(objectRef);
                return new ScopeObservedObjectEntry(
                    objectRef,
                    ScopeManifestCanonicalWriter.ComputeObjectRefSha256(objectRef));
            })
            .OrderBy(static entry => entry, ScopeObservedObjectComparer.Instance)
            .ToArray();
        VerifyObservedObjectTable(observedEntries);
        VerifyCompleteEnumeration(
            profile,
            observationResolver.CompleteEnumerationRef,
            observedEntries,
            observationResolver);

        var inputsByObject = new Dictionary<SourceObjectRef, ScopeObjectReductionInput>(
            ScopeObservedObjectComparer.Instance);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!inputsByObject.TryAdd(input.ObjectRef, input))
            {
                throw new ArgumentException(
                    "Reduction inputs must name unique objects.",
                    nameof(inputs));
            }
        }

        if (inputsByObject.Count != observedEntries.Length ||
            observedEntries.Any(entry => !inputsByObject.ContainsKey(entry.ObjectRef)))
        {
            throw new ArgumentException(
                "Reduction inputs must exactly equal the complete observed object set.",
                nameof(inputs));
        }

        var reducedRows = new ReducedScopeRow[observedEntries.Length];
        for (var ordinal = 0; ordinal < observedEntries.Length; ordinal++)
        {
            var entry = observedEntries[ordinal];
            reducedRows[ordinal] = ReduceCanonicalRow(
                profile,
                evidenceArtifacts,
                entry,
                ordinal,
                inputsByObject[entry.ObjectRef],
                observationResolver);
        }

        var rows = reducedRows.Select(static value => value.Row).ToArray();
        var accounting = CreateAccounting(reducedRows);
        var bodyCandidates = reducedRows
            .Where(value => value.Results.Any(result =>
                result.Axis == ScopeAxis.Body &&
                result.Disposition == ScopeDisposition.AcceptedSelected &&
                result.RoleMemberOrdinals.Contains(profile.BodyCandidateRoleMemberOrdinal)))
            .Select(static value => value.Row.ObjectOrdinal)
            .ToArray();
        var manifest = new ScopeManifest(
            ScopeManifestSchemaIds.Manifest,
            profile,
            observationResolver.CompleteEnumerationRef,
            evidenceArtifacts,
            observedEntries,
            rows,
            accounting,
            bodyCandidates);
        return VerifyAndOpen(manifest, observationResolver);
    }

    public static VerifiedScopeManifest VerifyAndOpen(
        ScopeManifest manifest,
        IScopeReductionEvidenceResolver observationResolver)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(observationResolver);
        VerifyObservedObjectTable(manifest.ObservedObjects);
        VerifyCompleteEnumeration(
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            manifest.ObservedObjects,
            observationResolver);
        if (manifest.Rows.Count != manifest.ObservedObjects.Count)
        {
            throw new InvalidOperationException(
                "Manifest rows do not cover the complete observed object table.");
        }

        var resultsByRow = new IReadOnlyList<ScopeAxisResult>[manifest.Rows.Count];
        for (var ordinal = 0; ordinal < manifest.Rows.Count; ordinal++)
        {
            var row = manifest.Rows[ordinal];
            if (row.ObjectOrdinal != ordinal)
            {
                throw new InvalidOperationException(
                    "Row object ordinals must be contiguous and equal their array position.");
            }

            var observed = manifest.ObservedObjects[ordinal];
            var evaluations = ExpandAndVerifyEvaluations(manifest.Profile, row);
            VerifySelectors(
                manifest.Profile,
                manifest.OrderedEvidenceArtifacts,
                observed,
                row.Selectors,
                observationResolver);
            VerifyRuleEvaluations(
                manifest.Profile,
                manifest.OrderedEvidenceArtifacts,
                observed,
                row.Selectors,
                evaluations,
                observationResolver);
            var results = SelectAllWinners(manifest.Profile, evaluations);
            VerifyAxisWinnerOrdinals(row.AxisWinningRuleOrdinals, results);
            var digest = ScopeManifestCanonicalWriter.ComputeExpandedRowSha256(
                manifest.Profile,
                manifest.OrderedEvidenceArtifacts,
                observed.ObjectRef,
                observed.ObjectRefSha256,
                row.Selectors,
                evaluations,
                results);
            if (!string.Equals(digest, row.RowSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A manifest row digest is incorrect.");
            }

            resultsByRow[ordinal] = results;
        }

        var usedEvidenceOrdinals = manifest.Rows
            .SelectMany(static row => row.Selectors)
            .Where(static selector => selector.EvidenceArtifactOrdinal is not null)
            .Select(static selector => selector.EvidenceArtifactOrdinal!.Value)
            .Distinct()
            .Order()
            .ToArray();
        if (!usedEvidenceOrdinals.SequenceEqual(
                Enumerable.Range(0, manifest.OrderedEvidenceArtifacts.Count)))
        {
            throw new InvalidOperationException(
                "The evidence-artifact table must contain exactly the referenced artifacts.");
        }

        VerifyAccounting(manifest, resultsByRow);
        VerifyBodyCandidates(manifest, resultsByRow);
        return new VerifiedScopeManifest(manifest);
    }

    internal static void VerifyCompleteEnumeration(
        ScopeProfileBinding profile,
        SourceArtifactRef completeEnumerationRef,
        IReadOnlyList<ScopeObservedObjectEntry> observedObjects,
        IScopeReductionEvidenceResolver evidenceResolver)
    {
        VerifyCompleteEnumeration(
            profile,
            completeEnumerationRef,
            observedObjects.Count,
            ScopeManifestCanonicalWriter.ComputeObservedObjectSequenceSha256(observedObjects),
            evidenceResolver);
    }

    internal static void VerifyCompleteEnumeration(
        ScopeProfileBinding profile,
        SourceArtifactRef completeEnumerationRef,
        int observedObjectCount,
        string observedObjectSequenceSha256,
        IScopeReductionEvidenceResolver evidenceResolver)
    {
        if (!ScopeValidation.ArtifactEquals(
                completeEnumerationRef,
                evidenceResolver.CompleteEnumerationRef))
        {
            throw new InvalidOperationException(
                "The manifest names a different complete-enumeration artifact than the resolver.");
        }

        var binding = new ScopeCompleteEnumerationBinding(
            completeEnumerationRef,
            profile.SourceProfileRef,
            profile.SelectorTableRef,
            observedObjectCount,
            observedObjectSequenceSha256);
        if (!evidenceResolver.IsCompleteEnumerationAdmitted(binding))
        {
            throw new InvalidOperationException(
                "The complete enumeration was not admitted against its exact observed-object set.");
        }
    }

    public static ScopeRequestReduction ReduceRequest(
        VerifiedScopeManifest verified,
        SourceObjectRef objectRef,
        IReadOnlyList<ScopeAxis> requestedAxes)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(objectRef);
        ArgumentNullException.ThrowIfNull(requestedAxes);
        if (requestedAxes.Count == 0)
        {
            throw new ArgumentException(
                "At least one scope axis must be requested.",
                nameof(requestedAxes));
        }

        var axes = requestedAxes
            .Select(axis => ScopeValidation.RequireDefined(axis, nameof(requestedAxes)))
            .Distinct()
            .OrderBy(static axis => (int)axis)
            .ToArray();
        if (axes.Length != requestedAxes.Count)
        {
            throw new ArgumentException(
                "Requested scope axes must be unique.",
                nameof(requestedAxes));
        }

        var manifest = verified.Manifest;
        var objectOrdinal = FindObjectOrdinal(manifest.ObservedObjects, objectRef);
        var row = manifest.Rows[objectOrdinal];
        var evaluations = ExpandAndVerifyEvaluations(manifest.Profile, row);
        var allResults = SelectAllWinners(manifest.Profile, evaluations);
        var selected = allResults.Where(result => axes.Contains(result.Axis)).ToArray();
        var composite = selected
            .Select(static result => result.Disposition)
            .OrderByDescending(DispositionRank)
            .First();
        var capabilities = composite == ScopeDisposition.AcceptedSelected
            ? IntersectCapabilities(selected)
            : Array.Empty<int>();
        return new ScopeRequestReduction(
            manifest.ObservedObjects[objectOrdinal].ObjectRef,
            axes,
            allResults,
            composite,
            capabilities);
    }

    internal static ReducedScopeRow ReduceCanonicalRow(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        ScopeObservedObjectEntry observed,
        int objectOrdinal,
        ScopeObjectReductionInput input,
        IScopeReductionEvidenceResolver observationResolver)
    {
        if (!ScopeObservedObjectComparer.Instance.Equals(observed.ObjectRef, input.ObjectRef))
        {
            throw new ArgumentException(
                "A reduction input names the wrong observed object.",
                nameof(input));
        }

        VerifySelectors(
            profile,
            evidenceArtifacts,
            observed,
            input.Selectors,
            observationResolver);
        var evaluations = OrderAndVerifyEvaluations(profile, input.RuleEvaluations);
        VerifyRuleEvaluations(
            profile,
            evidenceArtifacts,
            observed,
            input.Selectors,
            evaluations,
            observationResolver);
        var results = SelectAllWinners(profile, evaluations);
        var matched = evaluations
            .Where(static evaluation => evaluation.State == ScopeRuleEvaluationState.Matched)
            .Select(static evaluation => new ScopeMatchedEvaluation(
                evaluation.RuleOrdinal,
                evaluation.Effect!.Value,
                evaluation.Disposition!.Value,
                evaluation.RoleMemberOrdinals,
                evaluation.CapabilityMemberOrdinals))
            .ToArray();
        var winners = results.Select(static result => result.WinningRuleOrdinal).ToArray();
        var digest = ScopeManifestCanonicalWriter.ComputeExpandedRowSha256(
            profile,
            evidenceArtifacts,
            observed.ObjectRef,
            observed.ObjectRefSha256,
            input.Selectors,
            evaluations,
            results);
        var row = new ScopeManifestRow(
            objectOrdinal,
            input.Selectors,
            ScopeRuleBits.Encode(evaluations),
            matched,
            winners,
            input.FetchAddress,
            digest);
        return new ReducedScopeRow(row, results);
    }

    private static void VerifyObservedObjectTable(
        IReadOnlyList<ScopeObservedObjectEntry> entries)
    {
        ScopeObservedObjectEntry? previous = null;
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var digest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(entry.ObjectRef);
            if (!string.Equals(digest, entry.ObjectRefSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "An observed-object digest does not bind its exact object reference.");
            }

            if (previous is not null)
            {
                if (string.Equals(
                        previous.ObjectRefSha256,
                        entry.ObjectRefSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Observed object digests must be unique; a collision is not admissible.");
                }

                if (ScopeObservedObjectComparer.Instance.Compare(previous, entry) >= 0)
                {
                    throw new InvalidOperationException(
                        "Observed objects must be canonically sorted and unique.");
                }
            }

            previous = entry;
        }
    }

    private static void VerifySelectors(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        ScopeObservedObjectEntry observed,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        IScopeReductionEvidenceResolver observationResolver)
    {
        if (selectors.Count != profile.OrderedSelectorMemberOrdinals.Count)
        {
            throw new InvalidOperationException(
                "A row must carry exactly one evidence value for every ordered selector.");
        }

        for (var selectorOrdinal = 0; selectorOrdinal < selectors.Count; selectorOrdinal++)
        {
            var selector = selectors[selectorOrdinal];
            var selectorMember = profile.OrderedMembers[
                profile.OrderedSelectorMemberOrdinals[selectorOrdinal]];
            if (selector.RuleOrdinal is { } ruleOrdinal)
            {
                if (ruleOrdinal >= profile.OrderedRules.Count)
                {
                    throw new InvalidOperationException(
                        "A not-applicable selector names an unknown rule ordinal.");
                }

                var rule = profile.OrderedRules[ruleOrdinal];
                var notApplicableBinding = new ScopeSelectorNotApplicableBinding(
                    observed.ObjectRefSha256,
                    selectorOrdinal,
                    selectorMember,
                    profile.SourceProfileRef,
                    profile.SelectorTableRef,
                    ruleOrdinal,
                    profile.OrderedMembers[rule.RuleMemberOrdinal]);
                if (!observationResolver.IsSelectorNotApplicableAdmitted(notApplicableBinding))
                {
                    throw new InvalidOperationException(
                        "A selector non-applicability rule was not admitted against its exact binding.");
                }
            }

            if (selector.CauseMemberOrdinal is { } causeOrdinal)
            {
                ScopeValidation.RequireMemberOrdinal(
                    causeOrdinal,
                    profile.OrderedMembers,
                    profile.SourceProfileRef,
                    nameof(selectors));
            }

            if (selector.EvidenceArtifactOrdinal is not { } evidenceOrdinal)
            {
                continue;
            }

            if (evidenceOrdinal >= evidenceArtifacts.Count || selector.EvidenceKind is null)
            {
                throw new InvalidOperationException(
                    "A selector observation names an unknown evidence artifact.");
            }

            var binding = new ScopeSelectorObservationBinding(
                selector.EvidenceKind.Value,
                observed.ObjectRefSha256,
                selectorOrdinal,
                selectorMember,
                profile.SourceProfileRef,
                profile.SelectorTableRef,
                evidenceArtifacts[evidenceOrdinal],
                ScopeManifestCanonicalWriter.ComputeSelectorEvidenceSha256(
                    profile,
                    evidenceArtifacts,
                    selectorOrdinal,
                    selector));
            if (!observationResolver.IsSelectorObservationAdmitted(binding))
            {
                throw new InvalidOperationException(
                    "A selector observation was not admitted against its exact binding.");
            }
        }
    }

    private static void VerifyRuleEvaluations(
        ScopeProfileBinding profile,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts,
        ScopeObservedObjectEntry observed,
        IReadOnlyList<ScopeSelectorEvidence> selectors,
        IReadOnlyList<ScopeRuleEvaluation> evaluations,
        IScopeReductionEvidenceResolver evidenceResolver)
    {
        var selectorSetSha256 = ScopeManifestCanonicalWriter.ComputeSelectorSetSha256(
            profile,
            evidenceArtifacts,
            selectors);
        foreach (var evaluation in evaluations)
        {
            var rule = profile.OrderedRules[evaluation.RuleOrdinal];
            var binding = new ScopeRuleEvaluationBinding(
                observed.ObjectRefSha256,
                selectorSetSha256,
                evaluation.RuleOrdinal,
                profile.OrderedMembers[rule.RuleMemberOrdinal],
                profile.SourceProfileRef,
                profile.SelectorTableRef,
                ScopeManifestCanonicalWriter.ComputeRuleEvaluationSha256(profile, evaluation));
            if (!evidenceResolver.IsRuleEvaluationAdmitted(binding))
            {
                throw new InvalidOperationException(
                    "A rule evaluation was not admitted against its exact selectors and outcome.");
            }
        }
    }

    private static IReadOnlyList<ScopeRuleEvaluation> OrderAndVerifyEvaluations(
        ScopeProfileBinding profile,
        IReadOnlyList<ScopeRuleEvaluation> evaluations)
    {
        if (evaluations.Count == profile.OrderedRules.Count)
        {
            var alreadyOrdered = true;
            for (var ordinal = 0; ordinal < evaluations.Count; ordinal++)
            {
                var evaluation = evaluations[ordinal];
                ArgumentNullException.ThrowIfNull(evaluation);
                if (evaluation.RuleOrdinal != ordinal)
                {
                    alreadyOrdered = false;
                    break;
                }
            }

            if (alreadyOrdered)
            {
                for (var ordinal = 0; ordinal < evaluations.Count; ordinal++)
                {
                    var evaluation = evaluations[ordinal];
                    VerifyProfileMemberOrdinals(
                        profile,
                        evaluation.RoleMemberOrdinals,
                        nameof(evaluations));
                    VerifyProfileMemberOrdinals(
                        profile,
                        evaluation.CapabilityMemberOrdinals,
                        nameof(evaluations));
                }

                return evaluations;
            }
        }

        var ordered = new ScopeRuleEvaluation[profile.OrderedRules.Count];
        for (var index = 0; index < evaluations.Count; index++)
        {
            var evaluation = evaluations[index];
            ArgumentNullException.ThrowIfNull(evaluation);
            if ((uint)evaluation.RuleOrdinal < (uint)ordered.Length)
            {
                if (ordered[evaluation.RuleOrdinal] is not null)
                {
                    throw new ArgumentException(
                        "Rule evaluations contain a duplicate ordinal.",
                        nameof(evaluations));
                }

                ordered[evaluation.RuleOrdinal] = evaluation;
                continue;
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (evaluations[previous].RuleOrdinal == evaluation.RuleOrdinal)
                {
                    throw new ArgumentException(
                        "Rule evaluations contain a duplicate ordinal.",
                        nameof(evaluations));
                }
            }
        }

        if (evaluations.Count != profile.OrderedRules.Count)
        {
            throw new ArgumentException(
                "Every bound rule must be evaluated exactly once.",
                nameof(evaluations));
        }

        for (var ordinal = 0; ordinal < profile.OrderedRules.Count; ordinal++)
        {
            var evaluation = ordered[ordinal];
            if (evaluation is null)
            {
                throw new ArgumentException(
                    "Rule evaluations do not cover the ordered rule table.",
                    nameof(evaluations));
            }

            VerifyProfileMemberOrdinals(
                profile,
                evaluation.RoleMemberOrdinals,
                nameof(evaluations));
            VerifyProfileMemberOrdinals(
                profile,
                evaluation.CapabilityMemberOrdinals,
                nameof(evaluations));
            ordered[ordinal] = evaluation;
        }

        return ordered;
    }

    private static IReadOnlyList<ScopeRuleEvaluation> ExpandAndVerifyEvaluations(
        ScopeProfileBinding profile,
        ScopeManifestRow row)
    {
        var matches = ScopeRuleBits.Decode(
            row.RuleMatchBitsBase64Url,
            profile.OrderedRules.Count);
        var matchedByOrdinal = new Dictionary<int, ScopeMatchedEvaluation>();
        var previousOrdinal = -1;
        foreach (var evaluation in row.MatchedEvaluations)
        {
            if (evaluation.RuleOrdinal <= previousOrdinal ||
                evaluation.RuleOrdinal >= profile.OrderedRules.Count ||
                !matches[evaluation.RuleOrdinal] ||
                !matchedByOrdinal.TryAdd(evaluation.RuleOrdinal, evaluation))
            {
                throw new InvalidOperationException(
                    "Matched evaluations must exactly equal the set rule-match bits in order.");
            }

            VerifyProfileMemberOrdinals(
                profile,
                evaluation.RoleMemberOrdinals,
                nameof(row));
            VerifyProfileMemberOrdinals(
                profile,
                evaluation.CapabilityMemberOrdinals,
                nameof(row));
            previousOrdinal = evaluation.RuleOrdinal;
        }

        if (matchedByOrdinal.Count != matches.Count(static value => value))
        {
            throw new InvalidOperationException(
                "Every set rule-match bit requires exactly one matched payload.");
        }

        var result = new ScopeRuleEvaluation[profile.OrderedRules.Count];
        for (var ordinal = 0; ordinal < result.Length; ordinal++)
        {
            if (matches[ordinal])
            {
                var matched = matchedByOrdinal[ordinal];
                result[ordinal] = new ScopeRuleEvaluation(
                    ordinal,
                    ScopeRuleEvaluationState.Matched,
                    matched.Effect,
                    matched.Disposition,
                    matched.RoleMemberOrdinals,
                    matched.CapabilityMemberOrdinals);
            }
            else
            {
                result[ordinal] = new ScopeRuleEvaluation(
                    ordinal,
                    ScopeRuleEvaluationState.NotMatched,
                    null,
                    null,
                    [],
                    []);
            }
        }

        return result;
    }

    private static IReadOnlyList<ScopeAxisResult> SelectAllWinners(
        ScopeProfileBinding profile,
        IReadOnlyList<ScopeRuleEvaluation> evaluations)
    {
        var results = new ScopeAxisResult[ScopeValidation.AllAxes.Length];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = SelectWinner(
                profile,
                ScopeValidation.AllAxes[index],
                evaluations);
        }

        return results;
    }

    private static ScopeAxisResult SelectWinner(
        ScopeProfileBinding profile,
        ScopeAxis axis,
        IReadOnlyList<ScopeRuleEvaluation> evaluations)
    {
        ScopeRuleEvaluation? lowestMatch = null;
        ScopeRuleEvaluation? lowestHardMatch = null;
        for (var index = 0; index < evaluations.Count; index++)
        {
            var evaluation = evaluations[index];
            if (profile.OrderedRules[evaluation.RuleOrdinal].Axis != axis ||
                evaluation.State != ScopeRuleEvaluationState.Matched)
            {
                continue;
            }

            if (lowestMatch is null || evaluation.RuleOrdinal < lowestMatch.RuleOrdinal)
            {
                lowestMatch = evaluation;
            }

            if ((evaluation.Effect == ScopeRuleEffect.ExactDenial ||
                 evaluation.Disposition == ScopeDisposition.NeverIngest) &&
                (lowestHardMatch is null ||
                 evaluation.RuleOrdinal < lowestHardMatch.RuleOrdinal))
            {
                lowestHardMatch = evaluation;
            }
        }

        var winner = lowestHardMatch ?? lowestMatch ??
            throw new InvalidOperationException($"Scope axis {axis} has no matching rule.");
        if (axis != ScopeAxis.Body &&
            winner.RoleMemberOrdinals.Contains(profile.BodyCandidateRoleMemberOrdinal))
        {
            throw new InvalidOperationException(
                "The body-candidate role cannot appear on a non-body axis.");
        }

        return new ScopeAxisResult(
            axis,
            winner.RuleOrdinal,
            winner.Effect!.Value,
            winner.Disposition!.Value,
            winner.RoleMemberOrdinals,
            winner.CapabilityMemberOrdinals);
    }

    private static void VerifyAxisWinnerOrdinals(
        IReadOnlyList<int> ordinals,
        IReadOnlyList<ScopeAxisResult> results)
    {
        if (ordinals.Count != ScopeValidation.AllAxes.Length)
        {
            throw new InvalidOperationException(
                "Every row must carry four fixed-position axis winners.");
        }

        for (var index = 0; index < results.Count; index++)
        {
            if (ordinals[index] != results[index].WinningRuleOrdinal)
            {
                throw new InvalidOperationException(
                    "An axis winner does not equal the result derived from rule evaluations.");
            }
        }
    }

    private static IReadOnlyList<ScopeAccountingSet> CreateAccounting(
        IReadOnlyList<ReducedScopeRow> rows)
    {
        var result = new List<ScopeAccountingSet>(
            ScopeValidation.AllAxes.Length * ScopeValidation.AllDispositions.Length);
        foreach (var axis in ScopeValidation.AllAxes)
        {
            foreach (var disposition in ScopeValidation.AllDispositions)
            {
                result.Add(new ScopeAccountingSet(
                    axis,
                    disposition,
                    rows.Where(row => row.Results.Any(candidate =>
                            candidate.Axis == axis && candidate.Disposition == disposition))
                        .Select(static row => row.Row.ObjectOrdinal)
                        .ToArray()));
            }
        }

        return result;
    }

    private static void VerifyAccounting(
        ScopeManifest manifest,
        IReadOnlyList<ScopeAxisResult>[] resultsByRow)
    {
        var expectedCount = ScopeValidation.AllAxes.Length * ScopeValidation.AllDispositions.Length;
        if (manifest.Accounting.Count != expectedCount)
        {
            throw new InvalidOperationException("The manifest must carry all 16 accounting sets.");
        }

        var position = 0;
        foreach (var axis in ScopeValidation.AllAxes)
        {
            foreach (var disposition in ScopeValidation.AllDispositions)
            {
                var actual = manifest.Accounting[position++];
                if (actual.Axis != axis || actual.Disposition != disposition)
                {
                    throw new InvalidOperationException(
                        "Accounting entries must use the fixed axis-major order.");
                }

                var expected = resultsByRow
                    .Select((results, ordinal) => (results, ordinal))
                    .Where(value => value.results.Any(result =>
                        result.Axis == axis && result.Disposition == disposition))
                    .Select(static value => value.ordinal)
                    .ToArray();
                if (!actual.ObjectOrdinals.SequenceEqual(expected))
                {
                    throw new InvalidOperationException(
                        "An accounting set does not equal its exact derived partition.");
                }
            }
        }
    }

    private static void VerifyBodyCandidates(
        ScopeManifest manifest,
        IReadOnlyList<ScopeAxisResult>[] resultsByRow)
    {
        var expected = resultsByRow
            .Select((results, ordinal) => (results, ordinal))
            .Where(value => value.results.Any(result =>
                result.Axis == ScopeAxis.Body &&
                result.Disposition == ScopeDisposition.AcceptedSelected &&
                result.RoleMemberOrdinals.Contains(
                    manifest.Profile.BodyCandidateRoleMemberOrdinal)))
            .Select(static value => value.ordinal)
            .ToArray();
        if (!manifest.BodyCandidateOrdinals.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "Body candidates do not equal the exact accepted body-role projection.");
        }
    }

    private static int FindObjectOrdinal(
        IReadOnlyList<ScopeObservedObjectEntry> entries,
        SourceObjectRef requested)
    {
        var digest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(requested);
        var low = 0;
        var high = entries.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = entries[middle];
            // Lowercase hexadecimal digests are ASCII, so ordinal comparison is the
            // canonical UTF-8 order without per-comparison encoding allocations.
            var comparison = string.CompareOrdinal(candidate.ObjectRefSha256, digest);
            if (comparison < 0)
            {
                low = middle + 1;
            }
            else if (comparison > 0)
            {
                high = middle - 1;
            }
            else if (ScopeObservedObjectComparer.Instance.Equals(candidate.ObjectRef, requested))
            {
                return middle;
            }
            else
            {
                throw new InvalidOperationException(
                    "A requested object collided with a different canonical object digest.");
            }
        }

        throw new ArgumentException(
            "The requested object is absent from the verified manifest.",
            nameof(requested));
    }

    private static void VerifyProfileMemberOrdinals(
        ScopeProfileBinding profile,
        IReadOnlyList<int> ordinals,
        string parameterName)
    {
        foreach (var ordinal in ordinals)
        {
            ScopeValidation.RequireMemberOrdinal(
                ordinal,
                profile.OrderedMembers,
                profile.SourceProfileRef,
                parameterName);
        }
    }

    private static IReadOnlyList<int> IntersectCapabilities(
        IReadOnlyList<ScopeAxisResult> results)
    {
        var intersection = results[0].CapabilityMemberOrdinals.ToHashSet();
        foreach (var result in results.Skip(1))
        {
            intersection.IntersectWith(result.CapabilityMemberOrdinals);
        }

        return intersection.Order().ToArray();
    }

    private static int DispositionRank(ScopeDisposition disposition) => disposition switch
    {
        ScopeDisposition.AcceptedSelected => 1,
        ScopeDisposition.TypedQuarantine => 2,
        ScopeDisposition.Point => 3,
        ScopeDisposition.NeverIngest => 4,
        _ => throw new InvalidOperationException("Unknown scope disposition."),
    };

}

internal sealed record ReducedScopeRow(
    ScopeManifestRow Row,
    IReadOnlyList<ScopeAxisResult> Results);
