# SurveyApp.Tests — livrable 5 (tests unitaires xUnit et couverture)

Batterie xUnit couvrant le projet `SurveyApp`, avec **100 % de couverture de branche**
mesurée par coverlet et présentée par ReportGenerator.

## Reproduire la mesure

Trois commandes, depuis la racine du dépôt :

```bash
dotnet tool restore                                                  # installe ReportGenerator
dotnet test SurveyApp.slnx --collect:"XPlat Code Coverage"           # exécute et mesure
dotnet reportgenerator \
  -reports:"SurveyApp.Tests/TestResults/**/coverage.cobertura.xml" \
  -targetdir:"CoverageReport" \
  -reporttypes:"Html;TextSummary;Badges"
```

Le rapport s'ouvre dans `CoverageReport/index.html`, le résumé texte est dans
`CoverageReport/Summary.txt`. Les deux dossiers de sortie (`TestResults/`, `CoverageReport/`)
sont ignorés par git.

> Supprimer `SurveyApp.Tests/TestResults/` avant de relancer : ReportGenerator agrège **tous**
> les fichiers `coverage.cobertura.xml` présents, y compris ceux des exécutions précédentes.

## Résultat obtenu

```
Line coverage:   100%      (204 / 204)
Branch coverage: 100%      (58 / 58)
Method coverage: 100%      (21 / 21)
```

Aucun fichier n'est exclu de la mesure : `Program.cs` est couvert lui aussi, par les tests
d'intégration.

## Organisation

| Dossier | Contenu |
| --- | --- |
| `Infrastructure/` | Utilitaires de test : bac à sable disque, doubles, fabriques de données |
| `Models/` | Accesseurs des modèles |
| `Services/` | `ServiceSondage` — analyse du fichier, validation, persistance |
| `Controllers/` | `SondageController` — traduction des codes sentinelles en codes HTTP |
| `Security/` | `ClefAPIAuthz` — intergiciel de clé d'API |
| `Integration/` | Application complète en mémoire (`Program.cs`, pipeline, routage) |

## Deux points d'architecture à connaître avant de modifier ces tests

**1. Le répertoire courant est un état partagé.**
`ServiceSondage` code en dur des chemins relatifs (`"Data/sondage.txt"`, …) résolus contre le
répertoire courant du processus, et n'accepte aucune injection de chemin. Les tests basculent
donc le répertoire courant vers un dossier temporaire jetable (`DossierDonneesTemporaire`), ce
qui est un état global : la parallélisation xUnit est **désactivée** dans
`Infrastructure/AssemblyInfo.cs`. Sans cela les tests s'écraseraient mutuellement.

C'est aussi ce qui protège le dépôt : `Program.cs` tronque `Data/participants.json` et
`Data/reponsesRecues.json` dès son démarrage. Les tests d'intégration créent le bac à sable
**avant** de démarrer l'hôte, sinon un simple `dotnet test` effacerait les données versionnées.

**2. `Program` est `internal`.**
`Program.cs` utilise les instructions de haut niveau, donc la classe engendrée n'est pas
nommable depuis ce projet. Plutôt que de modifier le projet testé (ajout d'un
`InternalsVisibleTo`), `ApplicationDeTest` construit `WebApplicationFactory<>` par réflexion.
Si un `InternalsVisibleTo("SurveyApp.Tests")` est ajouté un jour à `SurveyApp`, cette
indirection pourra être remplacée par un simple `WebApplicationFactory<Program>`.

## Tests qui figent un comportement défectueux

Certains tests décrivent le comportement **réel** de l'application, pas le comportement
souhaité, parce qu'une suite qui échoue ne documente rien. Ils portent tous un suffixe explicite
et un commentaire. **Ils devront être inversés en même temps que la correction.**

| Test | Comportement figé |
| --- | --- |
| `GetSondage_DernierSondageDuFichier_RetourneNull_BogueDecalageIndice` | La garde est `liste.Count > id` au lieu de `>= id` : le dernier sondage du fichier est toujours inatteignable (404). |
| `ReponseSondage_QuestionsDansLeDesordre_RetourneMoinsTrois_BogueOrdreDInsertion` | La validation exige les clés `1..n` dans l'ordre d'insertion, alors qu'un objet JSON n'a pas d'ordre significatif. |
| `ReponseSondage_ClesInventees_PermettentDeRepondreIndefiniment_FailleV01` | L'unicité repose sur une clé choisie par l'appelant : changer de chaîne suffit à répondre à nouveau (livrable 4 non atteint). |
| `PostReponse_ClesInventees_ContournentLUnicite_FailleV01` | Idem, vérifié de bout en bout à travers le pipeline HTTP. |
| `ReponseSondage_ReponseValide_StockeLaCleEnClairDansLesReponsesRecues` | La clé est hachée dans `participants.json` mais stockée en clair dans `reponsesRecues.json`. |
| `InvokeAsync_ReponsesDErreur_SontDistinguables_FuiteDInformation` | 401 sans en-tête contre 403 avec mauvaise clé : confirme à un attaquant que le nom d'en-tête est correct. |
| `InvokeAsync_ClefNonConfiguree_LeveNullReference` | Une clé d'API absente de la configuration produit une 500 sur chaque requête au lieu d'un refus au démarrage. |
| `EnDeveloppement_DocumentSwagger_EstAccessibleSansClef` | Swagger est enregistré avant l'intergiciel de clé : la documentation est lisible sans authentification. |

## Limite connue de la mesure

La couverture de branche ne dit rien de la **concurrence**. `ServiceSondage` est un singleton
qui relit et réécrit intégralement ses fichiers JSON à chaque requête, sans verrou : des
soumissions simultanées se perdent mutuellement. Ces tests sont séquentiels par construction et
ne peuvent pas détecter ce défaut ; il est décrit dans `REVISION-SECURITE.md`.
