// The publisher's details the five legal pages print (the plan's item 7.1; the specification of record's R49, and the
// owner's item O5): the one data file the owner fills.
//
// Each value is a placeholder, «LIKE_THIS», until the owner replaces it with the real detail, between the quotes, and
// changes nothing else. The pages are built from these values (`legal-pages.mjs`, `LegalPage.jsx`), so a filled value
// reaches every page that says it, in both languages, at the next release. While any value is still its placeholder, the
// site cannot open: `deploy/deploy.ps1 -Open` and every `-Public` deployment ask the served pages first
// (`web/scripts/deploy-probe.mjs --opening`), and refuse while a page still holds one.
//
// This repository is public: a value filled here is published here as well as on the site.
export const LEGAL_PUBLISHER = Object.freeze({
  // The publisher of the site: the person who publishes Lex, its director of publication and the controller of the
  // personal data the privacy notice describes (O5 recommends the owner), as the pages name them: a full name.
  publisherName: "«PUBLISHER_NAME»",
  // The publisher's postal address, on one line (number and street, postcode and locality, and the country if not
  // Luxembourg), as the legal notice and the privacy notice print it.
  publisherAddress: "«PUBLISHER_ADDRESS»",
  // The one contact address every page gives: for the publisher, for errors in a text, for an accessibility barrier, for a
  // privacy request and for an objection by a person named in an act. A dedicated mailbox (O5).
  contactEmail: "«CONTACT_EMAIL»",
  // How long an e-mail to that address is kept once the matter it raises is closed, in whole months, as digits. The
  // privacy notice states it (GDPR art. 13(2)(a); R74: "a stated short period"). 12: the owner's decision of 2026-10-19.
  reportRetentionMonths: "12",
});
