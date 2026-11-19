// One real answer, recorded for How it works (the owner's brief of 2026-11-15, section 5: one real question followed
// through its stages). Captured on 2026-11-19 at 02:53 UTC by one request to the API at `origin` below,
// POST /api/v3/ask with the body `request` (ask/1's request: the question alone). The origin is the release candidate on the driver's PC (the same code, data and model as the release); the record is made again from the live site once the release serves.
//
// `envelope` is the answer as served, whole and unedited. `act` is the title the dossier of the same act gives the version
// the answer quotes (one dossier request, the same minute: `act.request`), since the answer names the act by its address
// only. Do not edit: record again with the same two requests (C:\lex-v3\driver-scripts\make-how-record.py), and date it.

export const HOW_ANSWER_RECORD = Object.freeze({
  "recordedAt": "2026-11-19T02:53:32Z",
  "origin": "http://127.0.0.1:5080",
  "request": {
    "operation_id": "ask",
    "parameters": {
      "question": "L'assureur peut-il résilier le contrat ?"
    }
  },
  "envelope": {
    "context": {
      "commits": {
        "index_builder_source_commit": "bfb839839ea7d49cec79e8525275968d1000be8e",
        "serving_runtime_source_commit": "5e02ec30c1bd5250404430d7728f3e86c4e34dce"
      },
      "freshness": {
        "channels": [
          {
            "age_days": 6,
            "lag_days": null,
            "lag_observations": 0,
            "newest_completed_observation": "2026-11-12T15:22:05Z",
            "publisher": "lu-legilux",
            "stale": false
          },
          {
            "age_days": 7,
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
        "observed_at": "2026-11-19T02:53:32.6053017Z",
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
    "request_ref": "http_e1731661d2ae895c3a783c775fded402ca5c889b1a404c1cf29eca4654eaa8a0",
    "result": {
      "object_type": "answer_dossier",
      "schema": "lex-v3-ask-result/1",
      "value": {
        "answer_locale": "fr",
        "gates": {
          "disclosure_complete": true,
          "disclosures": 15,
          "non_template_strings": 0,
          "quotes": 5
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
            "template_id": "notice.model_assisted",
            "text": "Un modèle d’IA a aidé à trouver les passages : parmi les lignes que le code de Lex lui a fournies, il a désigné celles qui répondent à votre question. Il n’a écrit aucune phrase de cette réponse : le code de Lex a vérifié chaque ligne désignée et écrit chaque phrase à partir de formules fixes ; les citations reproduisent le texte publié."
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
                "facts": [],
                "kind": "claim",
                "locale": "fr",
                "template_id": "answer.rule",
                "text": "La règle, dans les termes du texte :"
              },
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
                    "value": "art_39"
                  },
                  {
                    "kind": "law_words",
                    "name": "heading",
                    "value": "Cas, formes et effets de la résiliation"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-19"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.extract_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 39, « Cas, formes et effets de la résiliation », tel qu’il se lisait le 19 novembre 2026"
              },
              {
                "anchor": "art_39",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_39",
                "article_text_sha256": "5d934fca7b58b6845e17b144e019146d773cc0003b05d9d72b0c553166ae302d",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 73,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 587,
                "publisher": "lu-legilux",
                "quote_id": "q1",
                "shortened": true,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "s1",
                "text": "Hormis les cas prévus par la loi, l'assureur ne peut résilier le contrat.",
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
                    "value": "5d934fca7b58b6845e17b144e019146d773cc0003b05d9d72b0c553166ae302d"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 5d934fca7b58b6845e17b144e019146d773cc0003b05d9d72b0c553166ae302d"
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
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_39"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_39"
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
              },
              {
                "facts": [],
                "kind": "claim",
                "locale": "fr",
                "template_id": "answer.rule_cases",
                "text": "Cas prévus par le texte, relevés dans ce texte :"
              },
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
                    "value": "art_13"
                  },
                  {
                    "kind": "law_words",
                    "name": "heading",
                    "value": "Omission ou inexactitude non intentionnelles"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-19"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.extract_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 13, « Omission ou inexactitude non intentionnelles », tel qu’il se lisait le 19 novembre 2026"
              },
              {
                "anchor": "art_13",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_13",
                "article_text_sha256": "4550e706f866de2194664d9e8bd601694f6a1fcc84175b7091a5c720d1ae4a3b",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 125,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 394,
                "publisher": "lu-legilux",
                "quote_id": "q2",
                "shortened": true,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "s2",
                "text": "Si l'assureur apporte la preuve qu'il n'aurait en aucun cas assuré le risque, il peut résilier le contrat dans le même délai.",
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
                    "value": "4550e706f866de2194664d9e8bd601694f6a1fcc84175b7091a5c720d1ae4a3b"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 4550e706f866de2194664d9e8bd601694f6a1fcc84175b7091a5c720d1ae4a3b"
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
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_13"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_13"
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
                "object": "q2"
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
                "object": "q2"
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
                "object": "q2"
              },
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
                    "value": "art_22"
                  },
                  {
                    "kind": "law_words",
                    "name": "heading",
                    "value": "Mise en demeure et faculté de résiliation"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-19"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.extract_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 22, « Mise en demeure et faculté de résiliation », tel qu’il se lisait le 19 novembre 2026"
              },
              {
                "anchor": "art_22",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_22",
                "article_text_sha256": "893e87f998ecabdf1b8f4975d0a1312687ce72dad5a7624e399d06c5aaec5ea1",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 98,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 287,
                "publisher": "lu-legilux",
                "quote_id": "q3",
                "shortened": true,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "s3",
                "text": "L'assureur a le droit de résilier le contrat dix jours après l'expiration du délai visé ci-dessus.",
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
                    "value": "893e87f998ecabdf1b8f4975d0a1312687ce72dad5a7624e399d06c5aaec5ea1"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 893e87f998ecabdf1b8f4975d0a1312687ce72dad5a7624e399d06c5aaec5ea1"
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
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_22"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_22"
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
                "object": "q3"
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
                "object": "q3"
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
                "object": "q3"
              },
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
                    "value": "art_34"
                  },
                  {
                    "kind": "law_words",
                    "name": "heading",
                    "value": "Aggravation du risque"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-19"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.extract_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 34, « Aggravation du risque », tel qu’il se lisait le 19 novembre 2026"
              },
              {
                "anchor": "art_34",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_34",
                "article_text_sha256": "25d5c780ec1c90ff253b67bea7acbc349fe3e9eb9b47689141bffd21ebed21f6",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 262,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 894,
                "publisher": "lu-legilux",
                "quote_id": "q4",
                "shortened": true,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "s4",
                "text": "Si la proposition de modification du contrat d'assurance est refusée par le preneur ou si, au terme d'un délai d'un mois à compter de la réception de cette proposition, cette dernière n'est pas acceptée, l'assureur peut résilier le contrat dans les quinze jours.",
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
                    "value": "25d5c780ec1c90ff253b67bea7acbc349fe3e9eb9b47689141bffd21ebed21f6"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 25d5c780ec1c90ff253b67bea7acbc349fe3e9eb9b47689141bffd21ebed21f6"
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
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_34"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_34"
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
                "object": "q4"
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
                "object": "q4"
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
                "object": "q4"
              },
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
                    "value": "art_41"
                  },
                  {
                    "kind": "law_words",
                    "name": "heading",
                    "value": "Résiliation après sinistre"
                  },
                  {
                    "kind": "calendar_date",
                    "name": "date",
                    "value": "2026-11-19"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.extract_heading",
                "text": "/lu-legilux/loi-1997-07-27-n10, article 41, « Résiliation après sinistre », tel qu’il se lisait le 19 novembre 2026"
              },
              {
                "anchor": "art_41",
                "article_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_41",
                "article_text_sha256": "600725ace80b14e2ee971393a9157861927d8a7b5fc391a85aa556e3391e07bb",
                "date_role": "applicability",
                "from": "2025-11-25",
                "journal_officiel": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo",
                "kind": "quote",
                "language": "fra",
                "length": 306,
                "official_source": "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125",
                "offset": 38,
                "publisher": "lu-legilux",
                "quote_id": "q5",
                "shortened": true,
                "state_kind": "consolidation",
                "state_permalink": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra",
                "state_sha256": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
                "step_id": "s5",
                "text": "Sauf en assurance sur la vie et en assurance-maladie, le contrat peut réserver à l'assureur le droit de résilier le contrat après la survenance d'un sinistre donnant lieu à indemnisation. Cette résiliation est notifiée au preneur d'assurance dans le mois du premier paiement de la prestation de l'assureur.",
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
                    "value": "600725ace80b14e2ee971393a9157861927d8a7b5fc391a85aa556e3391e07bb"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.content_hash",
                "text": "Empreinte du contenu : 600725ace80b14e2ee971393a9157861927d8a7b5fc391a85aa556e3391e07bb"
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
                    "value": "/lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_41"
                  }
                ],
                "kind": "claim",
                "locale": "fr",
                "template_id": "law.permalink",
                "text": "Permalien : /lu-legilux/loi-1997-07-27-n10/2025-11-25--dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1/fra/art_41"
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
                "object": "q5"
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
                "object": "q5"
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
                "object": "q5"
              },
              {
                "facts": [],
                "kind": "claim",
                "locale": "fr",
                "template_id": "answer.more_articles",
                "text": "Autres articles de ce texte où le terme figure :"
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "anchor",
                      "name": "anchor",
                      "value": "art_79"
                    },
                    {
                      "kind": "work_address",
                      "name": "work",
                      "value": "/lu-legilux/loi-1997-07-27-n10"
                    },
                    {
                      "kind": "calendar_date",
                      "name": "date",
                      "value": "2026-11-19"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "action.read_article",
                  "text": "Lire l’article 79 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026"
                },
                "kind": "action",
                "operation_id": "evidence_bundle",
                "parameters": {
                  "article": "art_79",
                  "date": "2026-11-19",
                  "identifier": "/lu-legilux/loi-1997-07-27-n10"
                }
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "anchor",
                      "name": "anchor",
                      "value": "art_43"
                    },
                    {
                      "kind": "work_address",
                      "name": "work",
                      "value": "/lu-legilux/loi-1997-07-27-n10"
                    },
                    {
                      "kind": "calendar_date",
                      "name": "date",
                      "value": "2026-11-19"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "action.read_article",
                  "text": "Lire l’article 43 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026"
                },
                "kind": "action",
                "operation_id": "evidence_bundle",
                "parameters": {
                  "article": "art_43",
                  "date": "2026-11-19",
                  "identifier": "/lu-legilux/loi-1997-07-27-n10"
                }
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "anchor",
                      "name": "anchor",
                      "value": "art_57"
                    },
                    {
                      "kind": "work_address",
                      "name": "work",
                      "value": "/lu-legilux/loi-1997-07-27-n10"
                    },
                    {
                      "kind": "calendar_date",
                      "name": "date",
                      "value": "2026-11-19"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "action.read_article",
                  "text": "Lire l’article 57 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026"
                },
                "kind": "action",
                "operation_id": "evidence_bundle",
                "parameters": {
                  "article": "art_57",
                  "date": "2026-11-19",
                  "identifier": "/lu-legilux/loi-1997-07-27-n10"
                }
              },
              {
                "claim": {
                  "facts": [
                    {
                      "kind": "anchor",
                      "name": "anchor",
                      "value": "art_10"
                    },
                    {
                      "kind": "work_address",
                      "name": "work",
                      "value": "/lu-legilux/loi-1997-07-27-n10"
                    },
                    {
                      "kind": "calendar_date",
                      "name": "date",
                      "value": "2026-11-19"
                    }
                  ],
                  "kind": "claim",
                  "locale": "fr",
                  "template_id": "action.read_article",
                  "text": "Lire l’article 10 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026"
                },
                "kind": "action",
                "operation_id": "evidence_bundle",
                "parameters": {
                  "article": "art_10",
                  "date": "2026-11-19",
                  "identifier": "/lu-legilux/loi-1997-07-27-n10"
                }
              },
              {
                "facts": [],
                "kind": "claim",
                "locale": "fr",
                "template_id": "caveat.conditions_apply",
                "text": "Des conditions s’appliquent : elles figurent dans les lignes citées."
              },
              {
                "facts": [],
                "kind": "claim",
                "locale": "fr",
                "template_id": "caveat.cases_from_sweep",
                "text": "Cette liste vient d’un relevé des mots du terme dans le texte ; un cas rédigé autrement peut y manquer."
              }
            ],
            "class": "lookup",
            "cues": [
              {
                "function": "concludes_on_situation",
                "span": [
                  11,
                  18
                ]
              }
            ],
            "part_id": "p1",
            "span": [
              0,
              40
            ],
            "verdict": "answer"
          }
        ],
        "plan": {
          "plan_sha256": "2f6aa46ec7cb4332523aef8862d9283912d47582e4fd6e701d86dc62c3c3268f",
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
                  "kind": "question_span",
                  "name": "words",
                  "value": "résilier"
                },
                {
                  "kind": "dictionary_term",
                  "name": "term",
                  "value": "résiliation"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.term",
              "text": "Lex a lu vos mots « résilier » comme le terme « résiliation » de son vocabulaire de l’assurance."
            },
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.rule_question",
              "text": "Votre question porte sur la règle, sans élément d’un cas personnel : Lex cite la règle et ne dit pas si elle s’applique à une situation."
            },
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.kind.rule",
              "text": "Réponse cherchée : une règle, avec ses conditions et ses exceptions."
            },
            {
              "facts": [
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.date_today",
              "text": "Votre question ne donne pas de date : Lex a lu le droit tel qu’il se lit aujourd’hui, le 19 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "work_address",
                  "name": "work",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                },
                {
                  "kind": "count",
                  "name": "articles",
                  "value": "147"
                },
                {
                  "kind": "count",
                  "name": "lines",
                  "value": "913"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.scope",
              "text": "Texte lu : /lu-legilux/loi-1997-07-27-n10, tel qu’il se lisait le 19 novembre 2026 (articles : 147 ; lignes : 913)."
            },
            {
              "facts": [
                {
                  "kind": "count",
                  "name": "articles",
                  "value": "4"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.detector.title",
              "text": "Articles dont l’intitulé contient le terme : 4."
            },
            {
              "facts": [
                {
                  "kind": "count",
                  "name": "lines",
                  "value": "45"
                },
                {
                  "kind": "count",
                  "name": "articles",
                  "value": "25"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.detector.keyword",
              "text": "Lignes contenant une forme du terme : 45 ; articles concernés : 25."
            },
            {
              "facts": [
                {
                  "kind": "count",
                  "name": "lines",
                  "value": "30"
                },
                {
                  "kind": "count",
                  "name": "articles",
                  "value": "16"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.detector.cooccurrence",
              "text": "Lignes où le terme figure avec la partie nommée dans votre question ou avec une valeur : 30 ; articles concernés : 16."
            },
            {
              "facts": [
                {
                  "kind": "count",
                  "name": "lines",
                  "value": "31"
                },
                {
                  "kind": "count",
                  "name": "articles",
                  "value": "17"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.detector.type_pattern",
              "text": "Lignes ayant aussi la forme de la réponse cherchée : 31 ; articles concernés : 17."
            },
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.pass.sweep_cases",
              "text": "La règle renvoie aux cas prévus par la loi sans les nommer : Lex a parcouru tout le texte pour les relever."
            },
            {
              "facts": [],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.bricks.model.extraction",
              "text": "Un modèle d’IA a désigné, parmi les lignes fournies, celles qui répondent à votre question. Le code de Lex a vérifié chacune dans le texte avant de l’afficher."
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_39"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 39 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_13"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 13 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_22"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 22 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_34"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 34 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026."
            },
            {
              "facts": [
                {
                  "kind": "anchor",
                  "name": "anchor",
                  "value": "art_41"
                },
                {
                  "kind": "identifier_text",
                  "name": "identifier",
                  "value": "/lu-legilux/loi-1997-07-27-n10"
                },
                {
                  "kind": "calendar_date",
                  "name": "date",
                  "value": "2026-11-19"
                }
              ],
              "kind": "claim",
              "locale": "fr",
              "template_id": "plan.step.evidence_bundle_article",
              "text": "Lecture de l’article 41 de /lu-legilux/loi-1997-07-27-n10 tel qu’il se lisait le 19 novembre 2026."
            }
          ]
        },
        "schema": "lex-v3-answer-dossier/1",
        "trace": {
          "answer_locale": "fr",
          "context": [],
          "handoff_registry_sha256": "3fdb047bb32728a0a6ba91c367613dd749424cce0ba5f1f718c96716d1f1ccbb",
          "plan": {
            "bricks": {
              "activations": [
                {
                  "id": "title",
                  "on": true,
                  "reason": "act_has_headings"
                },
                {
                  "id": "keyword",
                  "on": true,
                  "reason": "row_has_concept"
                },
                {
                  "id": "cooccurrence",
                  "on": true,
                  "reason": "row_has_actor"
                },
                {
                  "id": "type_pattern",
                  "on": true,
                  "reason": "kind_has_pattern"
                },
                {
                  "id": "lexicon",
                  "on": true,
                  "reason": "row_has_actor"
                },
                {
                  "id": "sweep",
                  "on": true,
                  "reason": "proof_of_absence"
                },
                {
                  "id": "outline_descent",
                  "on": false,
                  "reason": "act_has_no_chapter_levels"
                },
                {
                  "id": "embeddings",
                  "on": false,
                  "reason": "off_by_default"
                },
                {
                  "id": "arbiter",
                  "on": false,
                  "reason": "code_lead_is_clear"
                },
                {
                  "id": "extraction",
                  "on": true,
                  "reason": "one_candidate_at_a_time"
                }
              ],
              "answer": {
                "caveats": [
                  "conditions_apply",
                  "cases_from_sweep"
                ],
                "complete": false,
                "found": true,
                "items": [
                  {
                    "extract": 0,
                    "role": "rule",
                    "step": "s1"
                  },
                  {
                    "extract": 0,
                    "role": "case",
                    "step": "s2"
                  },
                  {
                    "by": "code",
                    "extract": 0,
                    "role": "case",
                    "step": "s3"
                  },
                  {
                    "by": "code",
                    "extract": 0,
                    "role": "case",
                    "step": "s4"
                  },
                  {
                    "extract": 0,
                    "role": "case",
                    "step": "s5"
                  }
                ],
                "kind": "rule",
                "more": [
                  "art_79",
                  "art_43",
                  "art_57",
                  "art_10"
                ],
                "notes": [],
                "values": []
              },
              "articles": 147,
              "candidates": [
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words",
                    "direct_form",
                    "general_rule"
                  ],
                  "line": "art_39#8",
                  "score_tenths": 170
                },
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "special_heading",
                    "article_lead",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words",
                    "direct_form"
                  ],
                  "line": "art_41#3",
                  "score_tenths": 140
                },
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "special_heading",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words",
                    "direct_form"
                  ],
                  "line": "art_22#3",
                  "score_tenths": 125
                },
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "special_heading",
                    "article_lead",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_79#2",
                  "score_tenths": 125
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words",
                    "direct_form"
                  ],
                  "line": "art_13#6",
                  "score_tenths": 100
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words",
                    "direct_form"
                  ],
                  "line": "art_34#6",
                  "score_tenths": 100
                },
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "article_lead",
                    "answer_words"
                  ],
                  "line": "art_39#3",
                  "score_tenths": 100
                },
                {
                  "detectors": [
                    "keyword",
                    "title",
                    "special_heading",
                    "cooccurrence",
                    "type_pattern"
                  ],
                  "line": "art_41#7",
                  "score_tenths": 95
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_13#5",
                  "score_tenths": 85
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_34#5",
                  "score_tenths": 85
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_43#4",
                  "score_tenths": 85
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_43#5",
                  "score_tenths": 85
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern",
                    "answer_words"
                  ],
                  "line": "art_57#3",
                  "score_tenths": 85
                },
                {
                  "detectors": [
                    "keyword",
                    "cooccurrence",
                    "type_pattern"
                  ],
                  "line": "art_10#4",
                  "score_tenths": 70
                }
              ],
              "concepts": [
                {
                  "id": "resiliation",
                  "keyword": 51,
                  "source": "dictionary",
                  "span": [
                    19,
                    27
                  ]
                }
              ],
              "corpus_index": "fb0eb0f31bc448be7c0719d1744568f5e6ef6f36ef0872dfac6e1e3544614416",
              "date": "today",
              "detectors": [
                {
                  "articles": 4,
                  "id": "title",
                  "lines": 0
                },
                {
                  "articles": 25,
                  "id": "keyword",
                  "lines": 45
                },
                {
                  "articles": 16,
                  "id": "cooccurrence",
                  "lines": 30
                },
                {
                  "articles": 17,
                  "id": "type_pattern",
                  "lines": 31
                },
                {
                  "articles": 11,
                  "id": "answer_words",
                  "lines": 19
                },
                {
                  "articles": 85,
                  "id": "lexicon",
                  "lines": 192
                }
              ],
              "dictionary": "c1eae52ec71ed8996ff97c6a750640a868d291ccdb45a9168008bd5e236582ef",
              "kind": "rule",
              "lines": 913,
              "model_calls": [
                {
                  "input_tokens": 1862,
                  "model": "gpt-4.1-mini",
                  "output_tokens": 380,
                  "prompt": "649d1d6a7385fbb1",
                  "reply": "a91c039a0a82d183072d3f8b15bdd62a5757913c34bfea3438e12559650f3c6b",
                  "step": "extraction"
                }
              ],
              "passes": [
                {
                  "changed": "none",
                  "n": 1,
                  "outcome": "candidates_found",
                  "trigger": "first_pass"
                },
                {
                  "changed": "sweep_whole_act",
                  "n": 2,
                  "outcome": "cases_listed",
                  "trigger": "unnamed_cases"
                }
              ],
              "roles": [
                {
                  "id": "assureur",
                  "keyword": 0,
                  "source": "dictionary",
                  "span": [
                    2,
                    10
                  ]
                }
              ],
              "route": "rule",
              "route_reason": "kind_is_rule",
              "rule_question": true,
              "state": "dd8b4d17c1a650e2864e3551d25f109d4d5a33d81fc4a86853cb4dd0e001d1d1",
              "state_date": "2025-11-25",
              "sweep": [
                {
                  "articles": 19,
                  "keyword": 50,
                  "lines": 32
                },
                {
                  "articles": 25,
                  "keyword": 51,
                  "lines": 49
                }
              ],
              "unmatched": [],
              "version": "lex-v4-bricks/1",
              "work": "/lu-legilux/loi-1997-07-27-n10"
            },
            "follow_ups": [],
            "parts": [
              {
                "part_id": "p1",
                "proposed_verdict": "answer",
                "span": [
                  0,
                  40
                ]
              }
            ],
            "schema": "lex-v3-assistant-plan/2",
            "steps": [
              {
                "arguments": {
                  "article": {
                    "corpus": "art_39"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "corpus": "/lu-legilux/loi-1997-07-27-n10"
                  }
                },
                "extracts": [
                  {
                    "length": 73,
                    "offset": 587,
                    "role": "rule"
                  }
                ],
                "heading": [
                  10,
                  49
                ],
                "operation_id": "evidence_bundle",
                "part_id": "p1",
                "step_id": "s1",
                "text_sha256": "5d934fca7b58b6845e17b144e019146d773cc0003b05d9d72b0c553166ae302d"
              },
              {
                "arguments": {
                  "article": {
                    "corpus": "art_13"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "corpus": "/lu-legilux/loi-1997-07-27-n10"
                  }
                },
                "extracts": [
                  {
                    "length": 125,
                    "offset": 394,
                    "role": "case"
                  }
                ],
                "heading": [
                  10,
                  54
                ],
                "operation_id": "evidence_bundle",
                "part_id": "p1",
                "step_id": "s2",
                "text_sha256": "4550e706f866de2194664d9e8bd601694f6a1fcc84175b7091a5c720d1ae4a3b"
              },
              {
                "arguments": {
                  "article": {
                    "corpus": "art_22"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "corpus": "/lu-legilux/loi-1997-07-27-n10"
                  }
                },
                "extracts": [
                  {
                    "length": 98,
                    "offset": 287,
                    "role": "case"
                  }
                ],
                "heading": [
                  10,
                  51
                ],
                "operation_id": "evidence_bundle",
                "part_id": "p1",
                "step_id": "s3",
                "text_sha256": "893e87f998ecabdf1b8f4975d0a1312687ce72dad5a7624e399d06c5aaec5ea1"
              },
              {
                "arguments": {
                  "article": {
                    "corpus": "art_34"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "corpus": "/lu-legilux/loi-1997-07-27-n10"
                  }
                },
                "extracts": [
                  {
                    "length": 262,
                    "offset": 894,
                    "role": "case"
                  }
                ],
                "heading": [
                  10,
                  31
                ],
                "operation_id": "evidence_bundle",
                "part_id": "p1",
                "step_id": "s4",
                "text_sha256": "25d5c780ec1c90ff253b67bea7acbc349fe3e9eb9b47689141bffd21ebed21f6"
              },
              {
                "arguments": {
                  "article": {
                    "corpus": "art_41"
                  },
                  "date": {
                    "today": true
                  },
                  "identifier": {
                    "corpus": "/lu-legilux/loi-1997-07-27-n10"
                  }
                },
                "extracts": [
                  {
                    "length": 306,
                    "offset": 38,
                    "role": "case"
                  }
                ],
                "heading": [
                  10,
                  36
                ],
                "operation_id": "evidence_bundle",
                "part_id": "p1",
                "step_id": "s5",
                "text_sha256": "600725ace80b14e2ee971393a9157861927d8a7b5fc391a85aa556e3391e07bb"
              }
            ]
          },
          "plan_sha256": "2f6aa46ec7cb4332523aef8862d9283912d47582e4fd6e701d86dc62c3c3268f",
          "planner_id": "lex-v4-bricks/gpt-4.1-mini/prompt-44a95d72ed4c",
          "question": "L'assureur peut-il résilier le contrat ?",
          "question_sha256": "2a094f04c96b7354989c119fdeb4274cb56bd2275ae3cdb82eacffe9f961b580",
          "registry_sha256": "627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059",
          "schema": "lex-v3-answer-trace/1",
          "snapshot": {
            "snapshot_id": "corpus-b2c3c1ba55bdda9a",
            "snapshot_sha256": "b2c3c1ba55bdda9aab9039675d97e835765252e1f5703fbf713d083203d47bf7"
          },
          "steps": [
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_39",
                "date": "2026-11-19",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "24ea975d25bd4c62bf34c4a3992cf5fe9daf3560efb84ce41f2423365e5bd883",
              "step_id": "s1",
              "verdict": "answer"
            },
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_13",
                "date": "2026-11-19",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "8861340f0c946ee2f53d9511f23cc44ab17569720c9766d3dfa8ead2dd611dcf",
              "step_id": "s2",
              "verdict": "answer"
            },
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_22",
                "date": "2026-11-19",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "bc980e73ec729e06527bd99913bb0eed9642665ce09398a24fca06367177a13e",
              "step_id": "s3",
              "verdict": "answer"
            },
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_34",
                "date": "2026-11-19",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "6ece9723794d9a8f955f9672c4dfdf68b651757403ad79f9f1b8d86c9aff6095",
              "step_id": "s4",
              "verdict": "answer"
            },
            {
              "fired": true,
              "object_type": "evidence_bundle",
              "operation_id": "evidence_bundle",
              "outcome": "result",
              "parameters": {
                "article": "art_41",
                "date": "2026-11-19",
                "identifier": "/lu-legilux/loi-1997-07-27-n10"
              },
              "refusal_code": null,
              "result_sha256": "5e852999569bb8bd18cb3131ec6ea7a1ae41190fdf162e5ec7e217da3aab9913",
              "step_id": "s5",
              "verdict": "answer"
            }
          ],
          "templates_sha256": "897d4624f96b148a379d398723c331810c7de59b75ee1ffcc2637339d9b31a62",
          "today": "2026-11-19"
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
    "request_ref": "http_78fcf0a4225f1d2ecb2720235a926c5fdcdc0b556949093783f5ca9e3c18d100"
  }
});
