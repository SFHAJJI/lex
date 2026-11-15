// One real answer, recorded from the live site for How it works (the owner's brief of 2026-11-15, section 5: one real
// question followed through the six stages). Captured on 2026-11-15 at 13:51 UTC by one request to the live API,
// POST https://law.soufien.lu/api/v3/ask with the body `request` below (ask/1's request: the question alone).
//
// `envelope` is the answer as served, whole and unedited. `act` is the title the live dossier of the same act gives the
// version the answer quotes (one dossier request, the same minute: `act.request`), since the answer names the act by its
// address only. Do not edit: record again with the same two requests, and date the record.

export const HOW_ANSWER_RECORD = Object.freeze({
  "recordedAt": "2026-11-15T13:51:39Z",
  "origin": "https://law.soufien.lu",
  "request": {
    "operation_id": "ask",
    "parameters": {
      "question": "Quel est le délai de prescription d'un contrat d'assurance ?"
    }
  },
  "envelope": {
    "context": {
      "commits": {
        "index_builder_source_commit": "bfb839839ea7d49cec79e8525275968d1000be8e",
        "serving_runtime_source_commit": "38c1a47bac9af8ff055568523eb228b0b9f7066a"
      },
      "freshness": {
        "channels": [
          {
            "age_days": 2,
            "lag_days": null,
            "lag_observations": 0,
            "newest_completed_observation": "2026-11-12T15:22:05Z",
            "publisher": "lu-legilux",
            "stale": false
          },
          {
            "age_days": 3,
            "lag_days": null,
            "lag_observations": 0,
            "newest_completed_observation": "2026-11-11T19:24:48Z",
            "publisher": "eu-eurlex",
            "stale": false
          }
        ],
        "index_built_at": "2026-11-12T15:22:06Z",
        "last_checked_against_publisher": null,
        "maximum_data_age_days": 30,
        "observed_at": "2026-11-15T13:51:38.0819203Z",
        "stale": false,
        "upstream_health": "stale"
      },
      "jurisdiction": "lu",
      "provisional": false,
      "publisher": "lu-legilux",
      "snapshot": {
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "snapshot_sha256": "b2c3c1ba55bdda9aab9039675d97e835765252e1f5703fbf713d083203d47bf7"
      },
      "status": "success",
      "timeline_semantics": "publisher_applicability"
    },
    "object_type": "envelope",
    "operation_id": "ask",
    "refusal": null,
    "registry_schema": "lex-v3-operation-registry/2",
    "registry_sha256": "627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059",
    "request_ref": "http_f91cc8ef0fac2224ce50eb2849e4000ca45a9b91d42fcbefb82997e1b1961471",
    "result": {
      "object_type": "answer_dossier",
      "schema": "lex-v3-ask-result/1",
      "value": {
        "answer_locale": "fr",
        "gates": {
          "disclosure_complete": true,
          "disclosures": 3,
          "non_template_strings": 0,
          "quotes": 1
        },
        "label": {
          "facts": [],
          "kind": "claim",
          "locale": "fr",
          "template_id": "verdict.answer",
          "text": "D’après le texte officiel"
        },
        "notices": [
          {
            "facts": [],
            "kind": "claim",
            "locale": "fr",
            "template_id": "notice.preview",
            "text": "Phase d’essai : l’assistant n’a pas encore été examiné par un avocat ni par des annotateurs indépendants."
          },
          {
            "facts": [],
            "kind": "claim",
            "locale": "fr",
            "template_id": "notice.assembled_by_model",
            "text": "Un modèle d’IA a assemblé cette réponse : il a planifié les opérations de Lex à exécuter. Le code de Lex a vérifié ce plan, exécuté les opérations et écrit chaque phrase à partir de formules fixes ; les citations reproduisent le texte publié."
          },
          {
            "facts": [],
            "kind": "claim",
            "locale": "fr",
            "template_id": "notice.not_legal_advice",
            "text": "Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique."
          }
        ],
        "parts": [
          {
            "blocks": [
              {
                "facts": [
                  {
                    "kind": "work_address",
                    "name": "work",
                    "value": "/lu-legilux/loi-1997-07-27-n10"
                  },
                  {
                    "kind": "anchor",
                    "name": "anchor",
                    "value": "art_44"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-15"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.quote_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 44, tel qu’il se lisait le 15 novembre 2026"
              },
              {
                "anchor": "art_44",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_44",
                "article_text_sha256": "7cc0fbd0c28a19f26d27bca22532fab42e9c5cc59b2cd1c99ed53bd475cc1076",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 1888,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 0,
                "publisher": "lu-legilux",
                "quote_id": "q1",
                "shortened": false,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "f1",
                "text": "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.Le délai court à partir du jour de l'événement qui donne ouverture à l'action. Toutefois, lorsque celui à qui appartient l'action prouve qu'il n'a eu connaissance de cet événement qu'à une date ultérieure, le délai ne commence à courir qu'à cette date, sans pouvoir excéder cinq ans à dater de l'événement, le cas de fraude excepté.En matière d'assurance de la responsabilité, le délai court, en ce qui concerne l'action récursoire de l'assuré contre l'assureur, à partir de la demande en justice de la personne lésée, soit qu'il s'agisse d'une demande originaire d'indemnisation, soit qu'il s'agisse d'une demande ultérieure ensuite de l'aggravation du dommage ou de la survenance d'un dommage nouveau.En matière d'assurance de personnes, le délai court, en ce qui concerne l'action du bénéficiaire, à partir du jour où celui-ci a connaissance à la fois de l'existence du contrat, de sa qualité de bénéficiaire et de la survenance de l'événement duquel dépend l'exigibilité des prestations d'assurance.2.Sous réserve de dispositions légales particulières, l'action résultant du droit propre que la personne lésée possède contre l'assureur en vertu de l'article 89 se prescrit par cinq ans à compter du fait générateur du dommage ou, s'il y a infraction pénale à compter du jour où celle-ci a été commise.Toutefois, lorsque la personne lésée prouve qu'elle n'a eu connaissance de son droit envers l'assureur qu'à une date ultérieure, le délai ne commence à courir qu'à cette date, sans pouvoir excéder dix ans à compter du fait générateur du dommage ou, s'il y a infraction pénale, du jour où celle-ci a été commise.3.L'action récursoire de l'assureur contre l'assuré se prescrit par trois ans à compter du jour du paiement par l'assureur, le cas de fraude excepté.",
                "to": null,
                "work": "/lu-legilux/loi-1997-07-27-n10"
              },
              {
                "facts": [
                  {
                    "kind": "calendar_date",
                    "name": "from",
                    "value": "2025-11-25"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.interval_lu_open",
                "text": "Applicable à partir du 25 novembre 2025 (éditeur)."
              },
              {
                "facts": [
                  {
                    "kind": "content_hash",
                    "name": "hash",
                    "value": "7cc0fbd0c28a19f26d27bca22532fab42e9c5cc59b2cd1c99ed53bd475cc1076"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 7cc0fbd0c28a19f26d27bca22532fab42e9c5cc59b2cd1c99ed53bd475cc1076"
              },
              {
                "facts": [
                  {
                    "kind": "uri",
                    "name": "uri",
                    "value": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.official_source",
                "text": "Source officielle : http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125"
              },
              {
                "facts": [
                  {
                    "kind": "uri",
                    "name": "uri",
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_44"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_44"
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "uri",
                      "name": "uri",
                      "value": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "disclosure.authenticity_lu",
                  "text": "Ceci est la copie que Lex donne du texte, à titre d’information ; elle ne fait pas foi. Seuls font foi les textes publiés au Journal officiel (Mémorial A) : l’acte tel que publié, http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo."
                },
                "flag": "authenticity",
                "kind": "disclosure",
                "object": "q1"
              },
              {
                "claim": {
                  "facts": [],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "disclosure.no_legal_effect",
                  "text": "Un texte consolidé est un outil de documentation sans effet juridique."
                },
                "flag": "no_legal_effect",
                "kind": "disclosure",
                "object": "q1"
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "calendar_date",
                      "name": "date",
                      "value": "1997-09-07"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "disclosure.validity_conflict",
                  "text": "L’éditeur date cet article du 7 septembre 1997, une date différente de la date propre de la version ; les deux dates sont indiquées."
                },
                "flag": "validity_conflict",
                "kind": "disclosure",
                "object": "q1"
              }
            ],
            "class": "lookup",
            "cues": [],
            "part_id": "p1",
            "span": [
              0,
              59
            ],
            "verdict": "answer"
          }
        ],
        "plan": {
          "plan_sha256": "9419f6802bf602bd1b376e2df051d00c8c28abb8fd2dadc170e41ddd1850de2d",
          "rendered": [
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.heading",
              "text": "Ce que Lex a fait de votre question"
            },
            {
              "facts": [
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-15"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.date_today",
              "text": "Votre question ne donne pas de date : Lex a lu le droit tel qu’il se lit aujourd’hui, le 15 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "language_code",
                  "name": "language",
                  "value": "fra"
                },
                {
                  "kind": "question_span",
                  "name": "query",
                  "value": "délai de prescription d'un contrat d'assurance"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.search",
              "text": "Recherche de vos mots dans le texte que Lex détient en français : « délai de prescription d'un contrat d'assurance »."
            },
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.follow_up.single_article",
              "text": "Comme cette étape désignait un seul article, Lex a effectué l’étape suivante :"
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_44"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-15"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 44 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 15 novembre 2026."
            }
          ]
        },
        "schema": "lex-v3-answer-dossier/1",
        "trace": {
          "answer_locale": "fr",
          "context": [],
          "handoff_registry_sha256": "3fdb047bb32728a0a6ba91c367613dd749424cce0ba5f1f718c96716d1f1ccbb",
          "plan": {
            "follow_ups": [
              {
                "after": "s1",
                "arguments": {
                  "article": {
                    "pick": "sole_article",
                    "result": "s1"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "pick": "sole_work",
                    "result": "s1"
                  }
                },
                "condition": "on_single_article",
                "operation_id": "evidence_bundle",
                "step_id": "f1"
              }
            ],
            "parts": [
              {
                "part_id": "p1",
                "proposed_verdict": "answer",
                "span": [
                  0,
                  59
                ]
              }
            ],
            "schema": "lex-v3-assistant-plan/1",
            "steps": [
              {
                "arguments": {
                  "language": {
                    "choice": "fra"
                  },
                  "query": {
                    "span": [
                      12,
                      58
                    ]
                  }
                },
                "operation_id": "search",
                "part_id": "p1",
                "step_id": "s1"
              }
            ]
          },
          "plan_sha256": "9419f6802bf602bd1b376e2df051d00c8c28abb8fd2dadc170e41ddd1850de2d",
          "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
          "question": "Quel est le délai de prescription d'un contrat d'assurance ?",
          "question_sha256": "9488693ce3a8b254d215370daae1f31eee86a059ae375f965db7bcb12c7f60a2",
          "registry_sha256": "627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059",
          "schema": "lex-v3-answer-trace/1",
          "snapshot": {
            "snapshot_id": "corpus-b2c3c1ba55bdda9a",
            "snapshot_sha256": "b2c3c1ba55bdda9aab9039675d97e835765252e1f5703fbf713d083203d47bf7"
          },
          "steps": [
            {
              "fired": true,
              "object_type": "quote",
              "operation_id": "search",
              "outcome": "result",
              "parameters": {
                "language": "fra",
                "limit": 10,
                "query": "délai de prescription d'un contrat d'assurance"
              },
              "refusal_code": null,
              "result_sha256": "8d075f3191cf5132fcf1cedc1be789a333ee8c17260ec057645cd6253d7f27cf",
              "step_id": "s1",
              "verdict": "answer"
            },
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_44",
                "date": "2026-11-15",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "bdbe0de9bae4a3d1c788d7ee0da95735134147207591a93073ccf95ed144116b",
              "step_id": "f1",
              "verdict": "answer"
            }
          ],
          "templates_sha256": "fecb138f2e489b6bb59e26cf9ab1c014f314f16aaca46e1bfd3bdf239b5b0c7b",
          "today": "2026-11-15"
        },
        "verdict": "answer"
      }
    },
    "schema": "lex-v3-envelope/1",
    "verdict": "answer",
    "version": "v3"
  },
  "act": {
    "identifier": "/lu-legilux/loi-1997-07-27-n10",
    "expression_iri": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125/fr",
    "language": "fra",
    "title": "Version consolidée applicable au 25/11/2025 : Loi du 27 juillet 1997 sur le contrat d'assurance.",
    "recordedFrom": "dossier",
    "request": {
      "operation_id": "dossier",
      "parameters": {
        "identifier": "/lu-legilux/loi-1997-07-27-n10"
      }
    },
    "request_ref": "http_8429919e31c3073798c6cc512be74731fb74de3e272aa9445575841f592199c4"
  }
});
