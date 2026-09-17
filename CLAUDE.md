# APP1S8_Tristan — SurveyApp

**This is not Max's repo.** It is a local copy of the *other team's* project, kept beside
`C:\Users\Maxime\Documents\GitHub\APP1S8` (Max + F-O, `SONDAGEAPI`) for comparison.

The two teams sit the same course — **Secure Programming, APP1 S8** — and answer the same
*Guide de l'étudiant*: a secure .NET 10 survey API (*sondage*) plus supporting analysis, covering
the same **11 livrables** ("Requis 1" … "Requis 11"). See `../APP1S8/CLAUDE.md` for the
authoritative livrable table; it is not repeated here except where this project's state differs.

Authors, per `appsettings.json` and the Swagger description: **CANB1801** and **LANT1401**
(Tristan). Single commit `d29e695 "Tout"` on `main` over `5986723 Initial commit`, so there is no
meaningful history to read.

## Why this copy exists

Max asked Claude to read this project, understand it against the course requirements, and write down
everything insecure or improvable in it. The output of that pass is **`REVISION-SECURITE.md`** at
the repo root: a severity-ranked inventory of the vulnerabilities and the hardening opportunities,
mapped onto the 11 livrables, with what the project already gets right recorded alongside. Most
entries were reproduced against a running instance rather than inferred from reading.

**Nothing here is Max's to fix.** Treat the tree as read-only evidence:

- **Do** read it, compare designs, and explain where the two teams diverge.
- **Do** update `REVISION-SECURITE.md` if a new finding turns up.
- **Don't** edit `SurveyApp/` source, don't commit, don't push. It is someone else's graded work,
  and "helpfully fixing" it would be doing their assignment for them.
- The coach-not-implementer agreement from `../APP1S8/CLAUDE.md` applies here too, and harder.

## Repo layout

```
.gitignore                replaced 2026-09-17 with Max's (full dotnet template + SurveyApp block)
SurveyApp.slnx            repo root — solution, one project (no test project)
SurveyApp/                the API — net10.0, Microsoft.NET.Sdk.Web
  Program.cs              top-level statements; Swashbuckle wiring, DI, middleware pipeline
  Controllers/
    SondageController.cs  [ApiController], route api/[controller] — 2 actions
  Services/
    IServiceSondage.cs    GetSondage(int) / ReponseSondage(int, Reponse)
    ServiceSondage.cs     all the logic: text parsing, validation, dedup, JSON persistence
  Security/
    ClefAPIAuthz.cs       livrable 1 — raw middleware checking the X-Api-Key header
  Models/                 Sondage, Question, Choix, Reponse — plain mutable classes
  Data/                   the "database" — flat files, read and rewritten per request
    sondage.txt           the two surveys, in a bespoke text format, parsed on every read
    participants.json     SHA-256(cleParticipant) -> list of survey ids already answered
    reponsesRecues.json   the answers themselves, with cleParticipant in cleartext
    reponses.json         dead file — a sample request body, never read by the code
  appsettings.json        contains the API key in cleartext (see finding V-04)
  bin/, obj/              still tracked in git; .gitignore now covers them (see finding A-01)
```

## Design, in one paragraph

No EF Core, no database, no `HttpContext`-level authentication. `ServiceSondage` is a **singleton**
that, on every response submission, reads two JSON files and `sondage.txt` off disk, validates the
answer shape in-process, checks a dictionary for a prior participation, then rewrites both JSON
files whole with `File.WriteAllText`. Identity is a `cleParticipant` **string chosen by the caller
and placed in the request body** — hashed for storage, but never issued, never verified, never
bound to a session. That single decision is the root of the project's most serious problems.

This is the opposite end of the design space from `SONDAGEAPI`, which issues an opaque one-time
invitation token server-side, authenticates it through an `AuthenticationHandler`, and enforces
uniqueness with a conditional `UPDATE` inside SQLite. Comparing the two is the point of keeping
this copy.

## Endpoints

Every request passes through `ClefAPIAuthz` — except that `UseSwagger()`/`UseSwaggerUI()` are
registered *before* the middleware, so the UI is reachable without a key in Development.

| Endpoint | Auth | Notes |
| --- | --- | --- |
| `GET /` | none in Dev | Swagger UI — `RoutePrefix` is `String.Empty`, so it owns the root |
| `GET /api/sondage/{id:int}` | `X-Api-Key` | `id` is 1-based; **id 2 (the last survey) returns 404** — finding A-02 |
| `POST /api/sondage/{idSondage:int}/reponses` | `X-Api-Key` | body carries the participant's own identity |

Returns are hand-rolled sentinel ints mapped to status codes in the controller: `0`→200, `-1`→400,
`-2`→409, `-3`→404, `-4`→404. The 200 response body is the literal `0`, though the action is
declared `ActionResult<Reponse>`.

## Commands

```bash
dotnet build SurveyApp.slnx                       # builds clean: 0 errors, 11 nullable warnings
dotnet run   --project SurveyApp                  # https://localhost:5192 (profile is named "http")
```

There is no `dotnet test` — livrable 5 has no project to run. There is no `global.json`, so the
build floats on whatever SDK is installed.

`Program.cs` **truncates `Data/participants.json` and `Data/reponsesRecues.json` on every startup**,
before `builder.Build()`. Any run wipes the previous run's data; `git checkout` restores both files
to the empty state they are committed in. Relative paths are resolved against the content root,
which is the project directory under `dotnet run` — but not under `dotnet publish`.

The API key is `Equipe_Christine_Frechette_Coalition_Avenir_Quebec`, in cleartext in
`appsettings.json` and in git. It is not a secret in any useful sense, so there is nothing to
protect by keeping it out of this file.

## Livrable status

Compared against the same 11-row table Max's team uses.

| # | Livrable | State in this project |
| --- | --- | --- |
| 1 | HTTPS + access key | **Partial.** `UseHttpsRedirection()` present but only an HTTPS URL is bound, so it never fires; no HSTS. The key check works but leaks whether the key exists (401 vs 403), compares non-constant-time, and NREs if the setting is missing |
| 2 | OpenAPI via Swagger | **Done.** Swashbuckle 10.2.3, `SwaggerDoc` + `AddSecurityDefinition`/`AddSecurityRequirement` for the API key. `AddOpenApi()` is commented out. Dev-only |
| 3 | Postman collection | **Not started.** `SurveyApp.http` exists but points at `http://localhost:5192`, which is not bound, and holds only two `GET /` stubs |
| 4 | Participant authentication guaranteeing uniqueness | **Not met.** Dedup exists; authentication does not. Any caller can invent a `cleParticipant` and answer again — verified. See V-01 |
| 5 | xUnit battery in the same solution, full coverage | **Not started.** No test project, no coverage tooling |
| 6 | Security impact analysis | **Not started** |
| 7 | In-code security mechanisms (NX/DEP, ASLR, CFG) | **Not started.** The `csproj` only disables trimming |
| 8 | Obfuscation + configuration | **Not started** |
| 9 | Operational recommendations | **Not started** |
| 10 | CycloneDX SBOM | **Not started** |
| 11 | Security bug disclosure process | **Not started.** No `SECURITY.md` |

Roughly the same place as Max's team on the documents (6, 9, 11 all open on both sides), noticeably
behind on 4 and 5.

## Verified behaviour

Everything below was reproduced on 2026-09-17 against `dotnet run`, not inferred from reading:

- `GET /api/sondage/1` → 200; `GET /api/sondage/2` → **404**, although Sondage 2 exists in
  `sondage.txt`.
- No key → 401 with `Rentre ta clef??`; wrong key → **403** with `Mauvaise clef Kessé tu fâ`. The
  two are distinguishable, which confirms to an attacker that their header name is right.
- Header name matching is case-insensitive at runtime (`x-api-key` works), so the
  `X-Api-Key` / `X-API-Key` mismatch between the middleware and the Swagger definition is cosmetic.
- A duplicate `X-Api-Key` header yields 403 — `StringValues`' implicit string conversion returns
  `null` for multi-valued headers, so it fails closed.
- `POST` with `{}` → 400 from `[ApiController]` model validation; non-nullable reference types make
  `QR` and `cleParticipant` implicitly required, which accidentally closes the null-deref hole.
- `POST` with an invented `cleParticipant` → **200**. Repeat with a fresh string → 200 again,
  indefinitely.
- `QR` keys sent out of order (`4,3,2,1`) → **404**, because validation walks the dictionary in
  enumeration order and expects `1..n`.
- **12 concurrent submissions from 12 different participants → 2 stored, 10 × HTTP 500** with a
  full stack trace in the response body, and `participants.json` left holding 9 entries. Seven
  people were recorded as "has already answered" while their answer was discarded. This is the
  headline finding, F-03.

## Conventions in this codebase

Identifiers and user-facing strings are **French** (`ServiceSondage`, `clefAPIExtraite`,
`Rentre ta clef??`), which is a legitimate choice but differs from `SONDAGEAPI`, where only
user-facing strings are French. Models are mutable classes with public setters rather than records.
`Newtonsoft.Json` handles persistence while ASP.NET Core serialises responses with
`System.Text.Json` — two JSON stacks in one process, which matters for the livrable 10 SBOM.
