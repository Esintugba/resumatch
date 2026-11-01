# Skill catalog

Users pick skills from a curated catalog. User input never enters it.

- **Source:** [`API/Data/SkillCatalog/skill-catalog-v1.csv`](../../API/Data/SkillCatalog/skill-catalog-v1.csv), 399 skills written for this project. No row is copied from an external data set.
- **One name per technology:** variants such as `React.js`, `nodejs` or `postgres` are not separate rows. A test keeps normalized names unique across categories.
- **Names:** `Name` is the canonical name and is the only name used for matching. `NameTr` is a display name for general concepts, and stays empty for product names.
- **Changes:** edit the catalog with a new migration. Do not edit a migration that has already been applied.

## Search

`GET /api/skills?search=` matches any part of `Name` or `NameTr`. Exact matches come first, then names that start with the term, then the rest.

Search ignores case and Turkish letters: `İLETİŞİM`, `iletişim` and `iletisim` find the same skill. This applies to search only, not to matching.

Abbreviations are not resolved yet. `js`, `ts`, `k8s` and `golang` do not find JavaScript, TypeScript, Kubernetes or Go. A search for `postgres` works only because it is part of `PostgreSQL`.

## Catalog changes and the catalog hash

A ready parse draft stores a hash of the whole catalog. Adding, renaming or recategorizing a skill changes that hash, so such a draft can no longer be confirmed and returns `parse_draft_outdated`. The user requests a new parse. `NameTr` is not part of the hash.

Stored analyses are not affected. Their hash covers only the skills of the job posting they used.

## Job posting skills

With parsing off, job match scores depend on skills added by hand:

- `POST /api/job-postings/{id}/skills` adds a catalog skill with `skillId`, `requirementType` (default `Required`) and `importanceScore` from 0 to 100 (default 50).
- `DELETE /api/job-postings/{id}/skills/{skillId}` removes a skill that was added by hand.

Both need the current job posting `ETag` in `If-Match` and return the updated job posting with a new `ETag`. A job posting holds at most 50 skills. Rows that came from AI parsing keep their provenance: they cannot be overwritten by adding the same skill, and they cannot be removed here.

## Unlisted skills note

`unlistedSkillsNote` on a resume holds skills that are not in the catalog. It is plain text of up to 500 characters and is part of the resume, so it follows the resume `ETag`. It is not used by analysis or matching and never adds to the catalog. Clients must render it as text.
