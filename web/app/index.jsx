// One entry for everything the React side exports.
//
// The tests import the compiled form of this file rather than the JSX directly, because
// node --test cannot parse JSX and a loader hook would put a second, differently configured
// compiler between the tests and the code they are testing. Compiling once, the same way the
// build does, means the tests measure what ships.

export { Document, SyntheticBanner, SYNTHETIC_MARKER } from './Document.jsx';
export { renderDocument } from './render-document.mjs';
export { RefusalCard, Mark } from './RefusalCard.jsx';
export { Dossier } from './Dossier.jsx';
export { CSP_DIRECTIVES, FORBIDDEN_SOURCES, cspValue } from '../scripts/csp.mjs';
export { renderHydrationProof, hydrationTree, HYDRATION_FIXTURE } from './hydration-proof.jsx';
export { renderHydratableDocument } from './render-document.mjs';
export { AmbiguousVersion } from './AmbiguousVersion.jsx';
export { ResultList } from './ResultList.jsx';
export { FilterChips } from './FilterChips.jsx';
export { CompareArming, armedBy, armingRefusal, compareIfArmed, useCompareSelection } from './CompareArming.jsx';
export { DateField, parseAsOf, resolutionSentence } from './DateField.jsx';
export { SearchScreen } from './SearchScreen.jsx';
export { NoHitCard, Population, requirePopulation } from './NoHitCard.jsx';
export {
  RelaxationDisclosures,
  interpretationOf,
  requireRelaxationAccount,
  requireSameOriginSearchPath,
} from './RelaxationDisclosures.jsx';
export { BADGE_LABELS, Interpretation, REASON_EVIDENCES } from './ResultList.jsx';
export { renderSearchScreenPage } from './search-screen-preview.jsx';
