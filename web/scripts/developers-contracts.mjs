// The contracts the Developers page is generated from, read from this tree (`developers.mjs` says which and why): the envelope
// census, the census of answers and every schema file of `schemas/v3-platform`, each with the SHA-256 of its bytes. The live
// build reads them once, renders the page in each interface language from them, and publishes the schema files beside the
// pages; the tests read them the same way.

import { createHash } from "node:crypto";
import { readFile, readdir } from "node:fs/promises";

import { developersModel, publishedSchemaPath } from "./developers.mjs";

const PLATFORM = new URL("../../schemas/v3-platform/", import.meta.url);

/** Every schema file of `schemas/v3-platform`, in name order, as `{ file, bytes, json, sha256 }`. */
export async function readPlatformSchemas(directory = PLATFORM) {
  const files = (await readdir(directory)).filter((name) => name.endsWith(".schema.json")).sort();
  return Promise.all(files.map(async (file) => {
    const bytes = await readFile(new URL(file, directory));
    return { file, bytes, json: JSON.parse(bytes.toString("utf8")), sha256: createHash("sha256").update(bytes).digest("hex") };
  }));
}

/** The Developers page's facts, from the contracts this tree holds. */
export async function readDevelopersContracts(directory = PLATFORM) {
  const [census, answers, schemas] = await Promise.all([
    readFile(new URL("envelope-samples.json", directory), "utf8").then(JSON.parse),
    readFile(new URL("answer-samples.json", directory), "utf8").then(JSON.parse),
    readPlatformSchemas(directory),
  ]);
  return developersModel({ census, answers, schemas: schemas.map(({ file, json, sha256 }) => ({ file, json, sha256 })) });
}

/** The schema files as the site publishes them: each one's published path (no leading "/") and its bytes, unchanged. */
export async function publishedSchemaFiles(directory = PLATFORM) {
  return (await readPlatformSchemas(directory)).map(({ file, bytes }) => [publishedSchemaPath(file).slice(1), bytes]);
}
