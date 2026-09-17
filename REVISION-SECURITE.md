# Révision de sécurité et pistes d'amélioration — SurveyApp

**Projet révisé :** SurveyApp, équipe CANB1801 / LANT1401 · commit `d29e695 "Tout"`, branche `main`
**Date :** 2026-09-17 · **Révision faite par :** Claude, à la demande de Max (ParciIllusion)
**Cadre :** les 11 requis du *Guide de l'étudiant* (APP1 – S8, Module Sécurité informatique, p. 7)

Ce document recense **ce qui est exposé et ce qui peut être renforcé**, dans le contexte du cours de
programmation sécuritaire. Ce n'est ni une correction ni un jugement sur le travail : aucun fichier
de `SurveyApp/` n'a été modifié, le code compile (0 erreur, 11 avertissements) et le cas nominal
fonctionne. L'objectif est de fournir la matière brute des requis **6** (analyse d'impact et vecteurs
d'attaque) et **9** (recommandations opérationnelles), et de signaler les renforcements qui rapportent
le plus par rapport à l'effort.

**Méthode.** Chaque point marqué *vérifié* a été reproduit contre une instance en exécution
(`dotnet run --project SurveyApp`, `https://localhost:5192`, environnement Development) le
2026-09-17, avec `curl` — voir l'annexe. Les points marqués *lecture* viennent de l'analyse du code
seulement et sont présentés comme tels.

---

## 0. Ce que le projet fait déjà bien

À connaître autant que le reste : ce sont les arguments à défendre devant le correcteur, et il ne
faut pas les casser en corrigeant le reste.

- **La clé d'API est un intergiciel, pas un filtre par contrôleur.** `ClefAPIAuthz` est branché sur
  le pipeline, donc un futur contrôleur est protégé d'office. C'est le bon défaut : on ne peut pas
  oublier d'annoter une action.
- **L'intergiciel échoue du bon côté.** Un en-tête `X-Api-Key` envoyé en double donne 403 (*vérifié*).
  La conversion implicite de `StringValues` vers `string` renvoie `null` quand il y a plusieurs
  valeurs, donc l'ambiguïté est rejetée au lieu d'être devinée. Heureux hasard plutôt que choix
  délibéré, mais le comportement est le bon et mérite d'être nommé.
- **L'intention de hachage au repos est présente.** `participants.json` ne contient que des hachés.
  L'exécution est défaillante (V-04, V-05), mais le réflexe « ne pas stocker l'identifiant en clair »
  est là et c'est ce que le cours cherche à enseigner.
- **La granularité de l'unicité est correcte :** une participation par *(participant, sondage)*, pas
  une par participant tous sondages confondus. C'est la bonne lecture du requis 4.
- **La validation du modèle d'entrée fonctionne.** `[ApiController]` rejette `{}` en 400 avant
  d'atteindre la logique métier (*vérifié*), grâce aux types référence non-nullables. Voir A-07 : le
  résultat est bon, l'intention n'est pas explicite.
- **Aucun CORS n'est configuré.** Le défaut d'ASP.NET Core est de tout refuser — c'est le bon
  comportement, et c'est un choix à documenter dans le requis 9 plutôt qu'à subir. Y toucher
  « pour que ça marche dans le navigateur » ouvrirait une brèche.
- **La logique passe par une interface injectée** (`IServiceSondage`). La substitution est déjà
  possible, ce qui rendra la batterie de tests du requis 5 nettement moins coûteuse à écrire.
- **Le schéma de sécurité est décrit dans OpenAPI** (`AddSecurityDefinition` +
  `AddSecurityRequirement`), donc le requis 2 couvre bien l'authentification et pas seulement les
  routes.

---

## 1. Vulnérabilités

Regroupées par nature, avec la gravité dans le contexte du cours.

### A — Identité et authentification

#### V-01 · **Critique** · Aucune authentification du participant : l'unicité est contournable *(vérifié)*

`Models/Reponse.cs:6`, `Services/ServiceSondage.cs:22`

C'est le point central au regard du requis 4, qui demande « une authentification des participants
garantissant l'unicité de la participation ».

L'identité du participant est une chaîne que **le client choisit lui-même** et place dans le corps
de la requête :

```csharp
public string cleParticipant { get; set; }
...
string cleHashed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reponse.cleParticipant)));
```

Rien n'émet cette clé, rien ne la valide, rien ne la lie à une session. Le serveur la hache et
vérifie s'il a déjà vu ce haché. Conséquence directe :

```
POST /api/sondage/1/reponses  {"cleParticipant":"67", ...}                      -> 200
POST /api/sondage/1/reponses  {"cleParticipant":"67", ...}                      -> 409  (attendu)
POST /api/sondage/1/reponses  {"cleParticipant":"jinvente-nimporte-quoi", ...}  -> 200  (!!)
```

Quiconque possède la clé d'API peut répondre autant de fois qu'il le souhaite : il suffit de changer
la chaîne à chaque envoi. Le mécanisme réalise une **déduplication**, pas une authentification. Il
empêche un participant honnête de répondre deux fois par accident ; il n'empêche rien d'autre.

Le même défaut joue dans l'autre sens : un attaquant qui devine la clé d'un tiers (V-05) peut
**consommer sa participation** avant lui, et la victime se retrouve définitivement en 409.

La distinction à tenir à l'oral : *identifier* (déclarer qui on prétend être) n'est pas *authentifier*
(le prouver). Il manque la preuve.

**Renforcement.** Le serveur doit **émettre** un identifiant qu'il est seul à pouvoir produire, et le
recevoir ailleurs que dans un corps librement composé par le client :

- jeton opaque aléatoire (≥ 128 bits de `RandomNumberGenerator`), émis une fois, stocké haché,
  invalidé à l'usage, transmis dans un en-tête dédié ; ou
- jeton signé (JWT) avec durée de vie courte, vérifié par la plateforme ; ou
- rattachement à un compte déjà authentifié.

Le point commun des trois : le client ne peut pas fabriquer une identité valide. C'est aussi
l'occasion de passer par `AuthenticationHandler` plutôt que par un contrôle dans le service, pour que
`HttpContext.User` porte l'identité et que `[Authorize]` devienne utilisable (voir A-08).

#### V-02 · **Critique** · Concurrence : réponses perdues, participants bloqués, HTTP 500 *(vérifié)*

`Services/ServiceSondage.cs:20-100`, `Program.cs:14`

`ServiceSondage` est enregistré en **singleton** et exécute, sans aucun verrou, un cycle
lire-vérifier-écrire sur deux fichiers :

```csharp
var dataParticipants = File.ReadAllText("Data/participants.json");   // lecture
...
if (participants.ContainsKey(cleHashed) && participants[cleHashed].Contains(idSondage))
    return -2;                                                       // vérification
...
File.WriteAllText("Data/participants.json", jsonP);                  // écriture
File.WriteAllText("Data/reponsesRecues.json", jsonR);                // écriture
```

ASP.NET Core traite les requêtes en parallèle. Deux requêtes simultanées lisent le même état initial,
passent toutes les deux la vérification, puis se réécrivent l'une sur l'autre — ou entrent en
collision sur le handle de fichier. C'est un **TOCTOU** (*time-of-check to time-of-use*) de manuel,
appliqué au contrôle même qui porte le requis 4.

**Reproduction.** 12 participants *différents* (donc 12 réponses toutes légitimes), envoyés en
parallèle réel depuis un seul processus `curl --parallel --parallel-immediate` :

```
200 500 500 500 500 500 500 500 500 500 500 200
-> réponses réellement enregistrées : 2 sur 12
-> entrées dans participants.json  : 9
```

Trois défaillances distinctes dans un seul essai :

1. **Perte de données.** 10 réponses valides sur 12 ont disparu. `File.WriteAllText` réécrit la liste
   *entière* depuis un instantané périmé : le dernier écrivain écrase les autres.
2. **Incohérence entre les deux fichiers.** 9 participants marqués comme ayant répondu, 2 réponses
   stockées. Sept personnes sont **bloquées en 409 sans que leur réponse ait été comptée** — les
   résultats du sondage sont faux *et* les participants n'ont aucun recours. C'est une atteinte à
   l'intégrité, pas seulement une perte de disponibilité.
3. **HTTP 500.** `System.IO.IOException: The process cannot access the file
   'Data\participants.json' because it is being used by another process.`

Note méthodologique importante pour le requis 5 : le même essai lancé avec 25 processus `curl`
distincts n'a **pas** déclenché le problème (1 × 200, 24 × 409, 0 × 500), parce que le démarrage
échelonné des processus sous Windows sérialise les requêtes. **Une démonstration manuelle ne trouvera
jamais ce défaut.** Seul un test de concurrence automatisé le révèle — c'est l'argument le plus
solide en faveur du requis 5, et il vaut la peine de le dire explicitement dans le rapport.

**Renforcement.** Deux niveaux selon l'ambition :

- *Minimal* — un `lock` ou un `SemaphoreSlim` autour de la section critique supprime la corruption,
  mais sérialise tout le service et ne survit pas à plusieurs instances.
- *Correct* — un magasin qui offre l'atomicité : contrainte d'unicité en base, ou `UPDATE`
  conditionnel dont le nombre de lignes affectées tranche la course. SQLite suffit largement à
  l'échelle du projet.

Le fichier plat ne peut pas être rendu correct sans verrou : l'atomicité n'existe pas au niveau du
système de fichiers pour un cycle lire-modifier-écrire.

#### V-03 · **Élevée** · Le démarrage efface toutes les données de participation *(lecture)*

`Program.cs:9-10`

```csharp
File.WriteAllText("Data/participants.json", string.Empty);
File.WriteAllText("Data/reponsesRecues.json", string.Empty);
```

Ces deux lignes s'exécutent **avant `builder.Build()`**, à chaque démarrage. Tout redémarrage remet le
sondage à zéro : les réponses sont perdues et l'unicité de participation — le cœur du requis 4 — ne
survit pas à un `Ctrl+C`. Un participant qui a répondu hier peut répondre de nouveau aujourd'hui.

C'est visiblement un raccourci de développement pour repartir propre, mais dans l'état il annule le
mécanisme que le projet est censé démontrer. À conditionner au minimum à `IsDevelopment()`, et mieux,
à sortir du chemin de démarrage vers un script de remise à zéro explicite.

Deux effets de bord :

- `Data/` doit exister, sinon `DirectoryNotFoundException` **avant** que l'hôte ne soit construit,
  donc sans journalisation utile.
- Le contenu écrit est la chaîne vide, pas `"{}"` ni `"[]"`. `JsonConvert.DeserializeObject` renvoie
  `null` sur une chaîne vide ; le code s'en tire grâce aux `?? new ...`, mais un fichier JSON vide
  reste un format invalide qui piégera le prochain outil qui le lira.

### B — Secrets et données personnelles

#### V-04 · **Élevée** · Clé d'API en clair, versionnée *(lecture)*

`appsettings.json:10`

```json
"ApiKey": "Equipe_Christine_Frechette_Coalition_Avenir_Quebec"
```

Le secret qui protège toute l'API est en clair dans un fichier suivi par git, dans
`bin/Debug/net10.0/appsettings.json` (lui aussi versionné, voir A-01), et dans l'historique pour
toujours. Quiconque obtient le dépôt obtient l'API.

Pour un cours de programmation sécuritaire, c'est le constat qui coûte le plus cher pour la
correction la plus rapide : `dotnet user-secrets` en développement, variable d'environnement hors
développement. Il faut aussi considérer la clé actuelle comme **brûlée** et la remplacer, pas
seulement la déplacer — elle reste lisible dans `git log`.

Trois faiblesses structurelles du schéma, à nommer dans les requis 6 et 9 plutôt qu'à laisser
découvrir :

- la clé est **unique, statique et partagée** : aucune rotation, aucune expiration, aucune
  révocation, et aucun moyen de savoir laquelle des deux personnes de l'équipe l'a utilisée ;
- sa longueur et sa présence ne sont **jamais validées au démarrage** — rien n'empêche de déployer
  avec `"ApiKey": "a"` (voir aussi V-09) ;
- elle voyage dans un en-tête, donc elle apparaît dans les journaux de toute passerelle ou tout
  mandataire qui journalise les en-têtes.

#### V-05 · **Élevée** · Les réponses stockent la clé du participant en clair *(vérifié)*

`Services/ServiceSondage.cs:96`

`participants.json` ne stocke que des hachés — bonne intention. Mais `reponsesRecues.json` sérialise
l'objet `Reponse` entier, `cleParticipant` compris :

```json
[{"QR":{"1":"a","2":"c","3":"b","4":"d"},"cleParticipant":"67","IdSondage":1}]
```

Le haché ne protège donc rien : la valeur en clair est dans le fichier d'à côté, et il suffit de la
hacher pour faire la jointure. On obtient « qui a répondu quoi » en une ligne de script — sur un
sondage qui demande l'âge, le sexe, le journal lu et la consommation d'alcool, c'est-à-dire des
données personnelles sensibles au sens de la loi 25.

**Renforcement.** Si l'objectif est l'anonymat des réponses, la réponse stockée ne doit contenir
**aucun identifiant du répondant** : un fichier dit « cette personne a participé », l'autre dit « une
réponse anonyme a été reçue », et rien ne permet de relier les deux. Le modèle d'entrée et le modèle
de persistance doivent alors être deux types distincts — ce qui est de toute façon une bonne pratique
(voir A-05).

#### V-06 · **Moyenne** · Hachage non salé d'un secret à faible entropie *(lecture)*

`Services/ServiceSondage.cs:28`

```csharp
SHA256.HashData(Encoding.UTF8.GetBytes(reponse.cleParticipant))
```

SHA-256 nu : ni sel, ni poivre, ni étirement de clé. L'exemple fourni dans `Data/reponses.json` est
`"cleParticipant": "67"` — deux chiffres. Un attaquant qui met la main sur `participants.json`
retrouve l'espace complet des clés numériques en une fraction de seconde ; un dictionnaire de prénoms
ou de codes étudiants passe aussi vite.

Combiné à V-01, ce n'est pas seulement un problème de confidentialité : connaître la clé d'un tiers
permet de **répondre à sa place**.

**Renforcement.** Le choix dépend de la nature de la valeur, et c'est une bonne question d'examen :

- si la clé devient un **jeton aléatoire** de 128 bits ou plus (correction de V-01), SHA-256 est le
  bon outil — il n'y a rien à deviner, et le hachage rapide est un avantage ;
- si elle reste une valeur **devinable** (code étudiant, courriel), il faut un algorithme lent et
  salé — PBKDF2 (`Rfc2898DeriveBytes`), Argon2 ou bcrypt.

Autrement dit : corriger V-01 corrige V-06 par la même occasion, ce qui est un bon argument pour
traiter V-01 en premier.

### C — Transport et exposition

#### V-07 · **Élevée** · Divulgation de la trace d'appels complète *(vérifié)*

`Program.cs:47-56`

Aucun `UseExceptionHandler` n'est enregistré. En Development, la page d'exception du développeur est
active par défaut, et les 500 de V-02 renvoient **au client** la trace complète :

```
System.IO.IOException: The process cannot access the file
'C:\Users\Maxime\Documents\GitHub\APP1S8_Tristan\SurveyApp\Data\participants.json' ...
   at SurveyApp.Services.ServiceSondage.ReponseSondage(...)
```

Chemins absolus, arborescence du poste, noms internes des méthodes, version du framework — de la
reconnaissance offerte gratuitement. En Development c'est le comportement attendu de la plateforme,
mais il faut le savoir et l'écrire : **le correcteur fait tourner l'application en Development**. Et
comme aucun gestionnaire n'est branché du tout, rien ne produit non plus une réponse propre en
Production.

**Renforcement.** `app.UseExceptionHandler()` en dehors de Development, qui renvoie un
`ProblemDetails` neutre avec le `traceId` — l'identifiant permet de retrouver le détail dans les
journaux du serveur sans rien divulguer au client. À coupler avec V-11 (journalisation), sans quoi le
`traceId` ne mène nulle part.

#### V-08 · **Moyenne** · 401 contre 403 : l'API révèle si la clé existe *(vérifié)*

`Security/ClefAPIAuthz.cs:18-30`

```
sans en-tête       -> 401  "Rentre ta clef??"
mauvaise clé       -> 403  "Mauvaise clef Kessé tu fâ"
```

Deux réponses distinctes pour deux échecs d'authentification. Un attaquant apprend ainsi que son nom
d'en-tête est le bon et que le format de sa clé est accepté — il sait qu'il cherche au bon endroit.
C'est un **oracle** : petit, mais c'est exactement la classe de fuite que le requis 6 attend qu'on
sache nommer.

**Renforcement.** Une réponse **identique** dans les deux cas — 401, en-tête `WWW-Authenticate`,
corps neutre — de sorte qu'aucune différence observable ne distingue « clé absente » de « clé
fausse ».

Deux remarques sur le même bloc :

- Les messages `"Rentre ta clef??"` et `"Mauvaise clef Kessé tu fâ"` sont du texte brut, pas du
  `ProblemDetails` comme le reste de l'API. Indépendamment du ton, c'est une incohérence de contrat
  qu'il faudra régler avant la remise.
- La constante est `"X-Api-Key"` dans l'intergiciel et `"X-API-Key"` dans la définition Swagger
  (`Program.cs:29`). **Sans effet à l'exécution** : `IHeaderDictionary` compare sans tenir compte de
  la casse, `x-api-key` fonctionne (*vérifié*). Incohérence cosmétique, pas une faille — mais autant
  extraire une constante partagée.

#### V-09 · **Moyenne** · `NullReferenceException` si `ApiKey` est absente *(vérifié)*

`Security/ClefAPIAuthz.cs:22-25` — avertissement du compilateur **CS8602** :

```csharp
var clefAPI = configuration.GetValue<string>("ApiKey");   // null si non configurée
if (!clefAPI.Equals(clefAPIExtraite))                     // -> NullReferenceException
```

Retirer `ApiKey` de `appsettings.json` — ce qu'il faut faire pour corriger V-04 — transforme **chaque
requête authentifiée** en HTTP 500 avec la trace complète (V-07). **L'ordre des corrections compte :
traiter V-09 avant V-04.**

**Renforcement.** Valider la configuration **au démarrage** plutôt que requête par requête, pour que
l'application refuse de démarrer si le secret manque ou est trop court. Le mécanisme prévu par la
plateforme est un objet d'options avec `ValidateOnStart()`. C'est aussi le bon endroit pour imposer
une longueur minimale (V-04). Une application qui ne démarre pas est un incident visible ; une
application qui répond 500 à chaque requête est un incident qu'on découvre en production.

*Reproduit par `SurveyApp.Tests` :
`ClefAPIAuthzTests.InvokeAsync_ClefNonConfiguree_LeveNullReference`.*

#### V-10 · **Moyenne** · HTTPS non réellement forcé ; pas de HSTS *(vérifié)*

`Program.cs:58`, `Properties/launchSettings.json`

`UseHttpsRedirection()` est bien appelé, mais le seul profil de lancement s'appelle `"http"` et expose
`https://localhost:5192`. Aucun point d'écoute HTTP n'est lié : **la redirection ne se déclenche
jamais**. Le requis 1 est satisfait par accident de configuration plutôt que par mécanisme, et il
suffirait d'ajouter un `applicationUrl` en HTTP pour découvrir que rien ne protège.

Trois conséquences :

- `UseHsts()` n'est jamais appelé — aucun en-tête `Strict-Transport-Security`, donc aucune protection
  contre un premier accès en clair ni contre une rétrogradation.
- `SurveyApp.http` cible `http://localhost:5192`, qui n'est lié à rien : le fichier de requêtes
  fourni ne fonctionne pas tel quel.
- `UseHttpsRedirection()` est placé **après** `UseSwagger()`, donc la documentation ne bénéficierait
  pas de la redirection si un point d'écoute HTTP existait.

**À nommer explicitement dans le requis 6** : une redirection protège la requête *suivante*, jamais
celle qui vient d'arriver en clair. Une clé d'API envoyée sur HTTP est compromise avant que la
redirection n'ait lieu. C'est précisément ce que HSTS corrige, et c'est pour cela que les deux vont
ensemble.

#### V-11 · **Moyenne** · `AllowedHosts: "*"` et absence d'en-têtes de sécurité *(lecture)*

`appsettings.json:9`

`"AllowedHosts": "*"` désactive le filtrage de l'en-tête `Host`. L'application répond donc à
n'importe quel nom qui pointe vers elle, ce qui ouvre la porte au *DNS rebinding* et à
l'empoisonnement de cache par en-tête `Host` dès qu'un mandataire est placé devant. En développement
c'est sans conséquence ; c'est une ligne à changer et une phrase à écrire dans le requis 9.

Dans le même registre, aucun en-tête de durcissement n'est émis : pas de `X-Content-Type-Options:
nosniff`, pas de `Referrer-Policy`, pas de `Cache-Control: no-store` sur les réponses contenant des
données de sondage. Pour une API JSON pure l'exposition est faible, mais l'interface Swagger, elle,
est bien une page HTML servie par la même application.

#### V-12 · **Faible à moyenne** · Aucune limite de débit, de taille, ni journalisation *(lecture)*

Trois absences qui se renforcent l'une l'autre :

- **Aucune limitation de débit.** Rien ne borne le nombre de tentatives sur la clé d'API ni le nombre
  de soumissions. Combiné à V-01, une boucle de quelques lignes fausse complètement un sondage.
- **Aucune borne sur la taille des données.** `QR` est un `Dictionary` sans limite de cardinalité, et
  `cleParticipant` une chaîne sans longueur maximale. La limite par défaut de Kestrel (30 Mo) est le
  seul garde-fou, et chaque soumission acceptée est ajoutée à un fichier **réécrit en entier** à la
  soumission suivante (voir A-04) : le coût croît en O(n²) et le disque n'est jamais borné.
- **Aucune journalisation.** Aucun `ILogger` n'est injecté nulle part : aucun échec d'authentification
  n'est tracé, aucune soumission n'est horodatée. Rien ne permettrait de **constater** une attaque,
  encore moins de l'analyser après coup. C'est aussi ce qui rend le `traceId` de V-07 inutilisable.

**Renforcement.** `AddRateLimiter` côté applicatif (une fenêtre fixe par clé d'API suffit à la
démonstration), limitation en amont côté passerelle, bornes explicites sur le modèle d'entrée, et
journalisation structurée des rejets d'authentification. Matière directe pour le requis 6 (vecteur
d'attaque) et le requis 9 (recommandations opérationnelles).

---

## 2. Améliorations de code

Sans impact de sécurité direct, mais visibles à la correction, et plusieurs conditionnent les requis
restants.

#### A-01 · `bin/` et `obj/` versionnés malgré le `.gitignore` *(vérifié)*

`git ls-files` remonte 50 fichiers sous `SurveyApp/bin/` et `SurveyApp/obj/`, dont `SurveyApp.exe`,
`SurveyApp.dll`, `SurveyApp.pdb`, `apphost.exe` et les DLL de Swashbuckle et Newtonsoft.Json. Le
`.gitignore` contient pourtant `[Bb]in/` et `[Oo]bj/` — mais **les règles d'exclusion ne s'appliquent
pas aux fichiers déjà indexés**. Ils ont été ajoutés au premier commit et y restent.

Conséquences concrètes :

- un simple `dotnet build` salit l'arbre de travail — 23 fichiers modifiés sans une ligne de code
  touchée, ce qui s'est produit pendant cette révision ;
- chaque build produit un conflit binaire entre les deux coéquipiers ;
- `bin/Debug/net10.0/appsettings.json` contient **une deuxième copie de la clé d'API** (V-04) ;
- les `.pdb` embarquent les chemins absolus du poste et la correspondance symboles / code source —
  ce qui va directement à l'encontre du **requis 8** (protection du code par obfuscation) : publier
  les symboles annule l'obfuscation avant même de la configurer.

**Le `.gitignore` a été remplacé** (2026-09-17) par celui de l'équipe de Max — le modèle complet de
`dotnet new gitignore`, augmenté d'un bloc propre à SurveyApp. Il couvre désormais `bin/`, `obj/`,
`*.user`, `*.pdb`, le magasin de données d'exécution, et l'état local de JetBrains Rider (`.idea/`,
`.idea.*/`, `*.iml`), qui n'était couvert par aucune règle auparavant.

**Cela ne suffit pas** : les règles d'exclusion ne s'appliquent pas aux fichiers déjà indexés. **54
fichiers suivis** correspondent aux nouvelles règles et continueront d'être versionnés tant qu'ils
n'auront pas été retirés de l'index :

```bash
git rm -r --cached SurveyApp/bin SurveyApp/obj
git rm --cached SurveyApp/SurveyApp.csproj.user
git rm --cached SurveyApp/Data/participants.json SurveyApp/Data/reponsesRecues.json
git commit -m "Retirer les artefacts de build et l'etat local de l'index"
```

Les fichiers restent sur disque ; seul git cesse de les suivre. Les deux derniers sont le magasin
d'exécution, réécrit à chaque requête et vidé au démarrage (V-03) — `Data/sondage.txt`, lui, est une
donnée source et **reste suivi**. À faire en accord avec le coéquipier, et en vérifiant qu'aucune
donnée à conserver ne se trouve dans ces deux fichiers au moment du retrait.

#### A-02 · Erreur de bornes : le dernier sondage est introuvable *(vérifié)*

`Services/ServiceSondage.cs:8`

```csharp
if (liste.Count > id && id > 0)
    return liste[id - 1];
```

`id` est 1-basé, donc la borne doit être `liste.Count >= id`. Avec les deux sondages de
`sondage.txt`, `id = 2` donne `2 > 2` → faux → `null` → **404**.

```
GET /api/sondage/1  ->  200  {"id":1,"nom":"Sondage 1", ...}
GET /api/sondage/2  ->  404      <-- le sondage existe pourtant
```

Le *Sondage 2* est donc inaccessible, et impossible d'y répondre : `ReponseSondage` appelle le même
`GetSondage` et renvoie `-4` → 404. La moitié du jeu de données est morte, et c'est la première chose
qu'un correcteur essaiera. Un caractère à changer — et un cas de test évident pour le requis 5.

#### A-03 · Validation dépendante de l'ordre d'énumération d'un `Dictionary` *(vérifié)*

`Services/ServiceSondage.cs:40-52`

```csharp
int j = 1;
foreach (int question in reponse.QR.Keys)
{
    if (question != j) return -3;
    j++;
}
```

Le code exige que les clés arrivent dans l'ordre `1, 2, …, n`. Or l'ordre d'énumération d'un
`Dictionary<TKey, TValue>` **n'est pas garanti par le contrat de .NET** : il dépend de l'historique
des insertions et des collisions de hachage. Ici il suit l'ordre du JSON, donc un client qui envoie
ses réponses dans le désordre est rejeté :

```
{"QR":{"1":"a","2":"c","3":"b","4":"d"}}  -> 200
{"QR":{"4":"d","3":"b","2":"c","1":"a"}}  -> 404      <-- réponses identiques
```

L'ordre des membres d'un objet JSON n'a **aucune signification sémantique** (RFC 8259) : un client
parfaitement conforme peut les réordonner, et beaucoup de bibliothèques le font. La boucle suivante
(`sondage.Questions[i]` indexé par le rang d'énumération plutôt que par l'identifiant de la question)
repose sur la même hypothèse. Chercher la question par son `Id` supprime les deux problèmes d'un
coup.

#### A-04 · Entrées-sorties synchrones, relues et réécrites intégralement à chaque requête *(lecture)*

`Services/ServiceSondage.cs`

Chaque soumission effectue, sur le fil de traitement de la requête :

1. `File.ReadAllText` sur `participants.json` **et** `reponsesRecues.json` ;
2. `LireFichier("Data/sondage.txt")` — relecture et **réanalyse complète** du fichier de sondages,
   deux fois (une fois via `GetSondage`, et le service le rappelle) ;
3. `File.WriteAllText` des deux fichiers, **en entier**.

Trois problèmes distincts :

- **Appels bloquants dans un chemin `async`.** `File.ReadAllText` et `File.WriteAllText` sont
  synchrones ; sous charge ils immobilisent des fils du pool et provoquent une famine. Les variantes
  `…Async` existent et sont directement substituables.
- **Coût quadratique.** Réécrire toute la liste des réponses à chaque ajout donne un coût total en
  O(n²). Sur un sondage réel, c'est le facteur qui fait tomber le service avant tout le reste.
- **Analyse répétée d'un fichier immuable.** `sondage.txt` ne change pas en cours d'exécution ; il
  est pourtant relu et réanalysé à chaque appel. Une lecture unique au démarrage, mémorisée dans le
  singleton, supprime le travail et la moitié des accès disque.

Ces trois points sont la matière du volet *disponibilité* du requis 6 et des recommandations du
requis 9.

#### A-05 · Modèle d'entrée et modèle de persistance confondus *(vérifié)*

`Models/Reponse.cs`, `Controllers/SondageController.cs:33`

Le même type `Reponse` sert de corps de requête, de modèle métier et de format de stockage. Deux
conséquences observables :

- **`IdSondage` arrive deux fois** — dans la route (`{idSondage:int}`) et dans le corps. La
  validation utilise la route, la persistance stocke la valeur du corps, et rien ne vérifie qu'elles
  concordent :

  ```
  POST /api/sondage/1/reponses  {"IdSondage":999, ...}  -> 200
  reponsesRecues.json :  {"cleParticipant":"...","IdSondage":999, ...}
  ```

  La réponse est validée contre le sondage 1, comptée contre le sondage 1 dans `participants.json`,
  et archivée comme appartenant au sondage 999. Toute analyse ultérieure des résultats est fausse, et
  la divergence est silencieuse.
- **`cleParticipant` se retrouve dans le stockage** parce que le type entier est sérialisé — c'est
  le mécanisme de V-05.

Séparer les deux types (un DTO d'entrée, une entité de persistance) règle V-05 et A-05 en même temps,
et c'est la réponse classique au *mass assignment*. À défaut, retirer `IdSondage` du modèle d'entrée :
un champ que le client ne peut pas renseigner ne peut pas être mal renseigné.

#### A-06 · Codes de retour magiques et statuts HTTP incorrects *(vérifié)*

`Services/ServiceSondage.cs` renvoie `0`, `-1`, `-2`, `-3`, `-4`, que
`Controllers/SondageController.cs:29-45` traduit en cascade de `if` :

- **Les statuts ne correspondent pas à la sémantique HTTP.** `-3` (« les questions envoyées ne
  correspondent pas au sondage ») devient **404**, alors que le sondage existe : c'est une erreur de
  validation, donc 400. `-4` (sondage inexistant) devient aussi 404 — deux causes distinctes, un seul
  statut, impossible à diagnostiquer côté client. C'est A-03 qui rend la confusion visible.
- **Rien n'associe un code à son sens.** Une `enum` ou un type résultat rendrait les deux fichiers
  lisibles et empêcherait un décalage silencieux lors d'un ajout de cas.
- **Le succès renvoie 200 avec le corps `0`.** Une soumission crée une ressource : 201, ou à défaut
  204. Renvoyer `0` est un détail d'implémentation qui a fui jusqu'au client.
- Le dernier `else → NoContent()` est du **code mort** : la méthode ne renvoie jamais autre chose que
  `0`, `-1`, `-2`, `-3` ou `-4`.

#### A-07 · Le contrat OpenAPI ne décrit pas ce que l'API renvoie *(lecture)*

Les deux actions n'ont aucun attribut `[ProducesResponseType]`. Swagger déduit donc le contrat des
signatures, et se trompe :

- `ReponseSondage` est déclarée `ActionResult<Reponse>`, donc la documentation annonce un objet
  `Reponse` en 200 — alors que l'API renvoie l'entier `0` ;
- les statuts 400, 404 et 409, qui sont l'essentiel du comportement, n'apparaissent nulle part ;
- les réponses 401 et 403 de l'intergiciel de clé d'API ne figurent pas non plus.

Le **requis 3** demande une collection Postman *liée au schéma OpenAPI* : elle sera générée à partir
de ce document, donc un contrat faux donne une collection fausse. Corriger la documentation avant de
générer la collection évite de faire le travail deux fois.

À vérifier tôt : Swashbuckle 10.x s'appuie sur Microsoft.OpenApi v2 et émet du **OpenAPI 3.1** par
défaut. L'import de 3.1 dans Postman est historiquement moins fiable que celui de 3.0 — c'est la
raison pour laquelle l'équipe de Max a épinglé sa sortie en 3.0. Mieux vaut le découvrir maintenant
que la veille de la remise.

#### A-08 · Ordre inversé de `UseAuthorization` / `UseAuthentication` *(lecture)*

`Program.cs:59-60`

```csharp
app.UseAuthorization();
app.UseAuthentication();   // doit venir en premier
```

L'authentification établit *qui* appelle ; l'autorisation décide *ce qu'il a le droit de faire*.
Sans effet aujourd'hui — aucun schéma d'authentification n'est enregistré, les deux intergiciels sont
des passe-plats — mais dès que V-01 sera corrigé par une vraie authentification, le défaut devient
réel : l'autorisation s'exécuterait avant que l'identité n'existe. Et un correcteur qui lit
`Program.cs` le remarquera de toute façon.

Dans la même section : `app.UseSwagger()` est appelé **avant** `UseMiddleware<ClefAPIAuthz>()`, donc
l'interface Swagger et le document JSON sont accessibles **sans clé d'API**. Le tout est limité à
Development, ce qui est défendable — mais c'est un choix à assumer explicitement dans le requis 6
plutôt qu'à laisser découvrir.

#### A-09 · Analyse de `sondage.txt` sans validation *(vérifié)*

`Services/ServiceSondage.cs:104-150`

L'analyseur suppose un format parfait à chaque ligne :

```csharp
var qr = reste.Split("?");
question.Titre = qr[0] + "?";          // IndexOutOfRange si la question n'a pas de « ? »
...
reponses.Add(new Choix {
    Lettre = reponseParties[0].Trim(),
    Valeur = reponseParties[1].Trim()   // IndexOutOfRange si un choix n'a pas de « : »
});
...
sondage.Id = int.Parse(sondage.Nom.Replace("Sondage ", ""));   // FormatException
```

Le fichier est contrôlé par le serveur, donc la gravité est faible aujourd'hui — mais toute
défaillance devient un HTTP 500 avec trace complète (V-07), et la moindre faute de frappe en ajoutant
un sondage met l'API à terre. Surtout, c'est l'exemple type de ce que le cours enseigne : **analyser
une entrée sans valider sa forme**. Le jour où ce fichier devient téléversable par un administrateur,
c'est une faille. Un format structuré (JSON) supprimerait l'analyseur entier, ou à défaut
`TryParse` + vérification des longueurs, avec un échec au démarrage plutôt qu'à la requête.

*Les trois modes de défaillance sont désormais reproduits par `SurveyApp.Tests` :
`GetSondage_ChoixSansDeuxPoints_LeveIndexOutOfRange`,
`GetSondage_EnteteAvecNumeroNonEntier_LeveFormatException` et
`GetSondage_QuestionAvantToutEntete_LeveInvalidOperation` — ce dernier n'était pas relevé
plus haut : une ligne de question placée avant tout en-tête `Sondage` fait appeler
`sondages.Last()` sur une liste vide. L'absence de `Data/sondage.txt` remonte de même une
`FileNotFoundException` brute (`GetSondage_FichierAbsent_LeveUneException`).*

#### A-10 · Avertissements, fichier mort, dépendance JSON en double *(vérifié)*

`dotnet build` réussit avec **11 avertissements**, tous de nullabilité :

- 8 × **CS8618** — propriétés non-nullables jamais initialisées, dans les quatre modèles ;
- 1 × **CS8603** (`ServiceSondage.cs:18`) — `GetSondage` est déclarée `Sondage` mais renvoie `null` ;
- 1 × **CS8602** (`ClefAPIAuthz.cs:25`) — le déréférencement de V-09 ;
- 1 × **CS8600** (`ServiceSondage.cs:105`) — `string ligne` affecté depuis `ReadLine()`.

Les CS8618 ont un effet de bord **favorable** qu'il vaut la peine de connaître : comme `QR` et
`cleParticipant` sont non-nullables, `[ApiController]` les traite comme obligatoires et rejette `{}`
en 400 avant même d'entrer dans le service (*vérifié*). La nullabilité fait ici le travail d'une
validation explicite — mais par accident, pas par conception. Des attributs `[Required]`, ou des
`record` avec `required`, rendraient l'intention lisible et survivraient à un changement de
configuration. Activer `<TreatWarningsAsErrors>` empêcherait la dérive.

Deux points connexes :

- **`Data/reponses.json` est un fichier mort.** Aucun code ne le lit : c'est un exemple de corps de
  requête rangé au milieu du magasin de données. Sa place est dans `SurveyApp.http` ou dans la
  collection Postman du requis 3.
- **Deux bibliothèques JSON dans le même processus.** `Newtonsoft.Json` 13.0.4 pour la persistance,
  `System.Text.Json` (via le framework) pour la sérialisation HTTP. Ça fonctionne, mais c'est une
  dépendance et une surface d'attaque de plus dans le SBOM du **requis 10**, pour un usage que
  `System.Text.Json` couvre entièrement. À justifier ou à retirer.

---

## 3. Requis non entamés — points d'entrée concrets

Requis 3, 6, 7, 8, 9, 10 et 11 n'ont rien dans le dépôt. Ce ne sont pas des failles, mais c'est là
que se trouve le plus de points disponibles — environ la moitié de la note est documentaire.

Le requis 5 est **livré** depuis le 2026-09-17 : projet `SurveyApp.Tests` dans `SurveyApp.slnx`,
82 tests, **100 % de couverture de branche** (58/58) mesurée par coverlet et présentée par
ReportGenerator. Voir `SurveyApp.Tests/README.md`. Une limite subsiste, notée ci-dessous.

| # | Requis | Point d'entrée le plus court |
| --- | --- | --- |
| 3 | Collection Postman liée au schéma OpenAPI | Importer `/swagger/v1/swagger.json` dans Postman plutôt que composer les requêtes à la main. Corriger A-07 d'abord, et vérifier la question 3.0 / 3.1 |
| 5 | ~~Batterie xUnit dans la même solution~~ **Fait** | `SurveyApp.Tests`, 82 tests, 100 % ligne et branche, `Program.cs` compris. Reste à faire : **un test de concurrence**, seul moyen de démontrer V-02 — volontairement absent car il serait non déterministe tant que V-02 n'est pas corrigé (voir plus bas) |
| 6 | Analyse d'impact et vecteurs d'attaque | Les sections 1 et 2 de ce document sont directement réutilisables. Ordonner par couple *probabilité × impact*, et nommer explicitement V-01, V-02, V-04, V-07 |
| 7 | Mécanismes de sécurité dans le code | Propriétés du `csproj` et options de l'éditeur de liens : DEP/NX, ASLR haute entropie, Control Flow Guard. Y ajouter le durcissement de compilation : `<TreatWarningsAsErrors>`, `<EnableNETAnalyzers>`, `<AnalysisMode>All</AnalysisMode>` |
| 8 | Obfuscation + configuration utilisée | Un outil (Obfuscar, par exemple) et son fichier de configuration versionné. **Prérequis : A-01** — publier les `.pdb` annule l'obfuscation |
| 9 | Recommandations opérationnelles | Architecture cible : passerelle terminant TLS, secrets dans un coffre, base transactionnelle à la place des fichiers plats, journalisation centralisée, limitation de débit. V-02, V-04, V-11 et V-12 fournissent chacun un paragraphe |
| 10 | SBOM CycloneDX | `dotnet CycloneDX` sur la solution. Le rapport sera d'autant plus court que A-10 aura retiré Newtonsoft.Json. À compléter par `dotnet list package --vulnerable --include-transitive` |
| 11 | Processus de signalement et de publication | Un `SECURITY.md` à la racine : versions supportées, canal de signalement privé, délai de réponse, délai de divulgation. C'est le requis le moins coûteux du lot |

**Sur le test de concurrence manquant.** Un test qui *démontre* V-02 doit affirmer que des
soumissions simultanées se perdent — donc affirmer un comportement défectueux dont la
manifestation dépend de l'ordonnancement. Sur une machine rapide, les requêtes peuvent se
sérialiser et le test passerait à tort (c'est exactement ce que montre l'annexe : 25 processus
`curl` distincts donnent 1 × 200 et 24 × 409, sans aucune erreur). L'ordre utile est donc :
corriger V-02 d'abord — verrou ou base transactionnelle — **puis** ajouter un test de
concurrence qui affirme la propriété *correcte* : « N participants distincts en parallèle
⇒ exactement N réponses enregistrées, aucune 500 ». Ce test-là est déterministe, et il
protège durablement la correction.

---

## 4. Ordre suggéré

Par rapport effet / effort, en tenant compte des dépendances :

1. **A-02** — un caractère (`>` → `>=`), débloque la moitié du jeu de données. À faire tout de suite.
2. **A-01**, puis **V-09**, puis **V-04** — sortir `bin`/`obj` de git, valider la configuration au
   démarrage, et seulement ensuite sortir la clé de `appsettings.json`. **L'ordre compte** : retirer
   la clé avant de corriger V-09 transforme chaque requête en 500.
3. **V-01** — le requis 4 n'est pas satisfait sans ça. C'est le plus gros morceau restant, il corrige
   V-06 au passage, et il conditionne ce que les tests du requis 5 auront à prouver.
4. **V-02** et **V-03** — sans eux, l'unicité mise en place par V-01 ne tient ni sous charge ni au
   redémarrage.
5. **V-05** et **A-05** — même correction : séparer le modèle d'entrée du modèle de persistance.
6. **Requis 6** — l'analyse d'impact. Elle définit les mitigations que le requis 5 doit prouver :
   l'écrire avant les tests évite d'écrire les tests deux fois. La matière est déjà là.
7. **Requis 5** — la batterie xUnit, avec le test de concurrence en pièce maîtresse.
8. **Requis 11**, puis **10**, puis **3** — les moins coûteux du lot restant.
9. **Requis 7**, **8**, **9** — durcissement, obfuscation, architecture.
10. Le reste des améliorations (A-03, A-04, A-06 à A-10) au fil de l'eau.

---

## Annexe — reproduction

Instance lancée avec `dotnet run --project SurveyApp` (`https://localhost:5192`, environnement
Development), certificat de développement accepté via `curl -k`.

```bash
B="https://localhost:5192/api/sondage"
K="Equipe_Christine_Frechette_Coalition_Avenir_Quebec"

# A-02 — le dernier sondage est introuvable
curl -sk -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $K" $B/1     # 200
curl -sk -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $K" $B/2     # 404

# V-08 — 401 contre 403
curl -sk -o /dev/null -w "%{http_code}\n"                   $B/1      # 401
curl -sk -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: x" $B/1      # 403

# V-08 (note) — comparaison insensible à la casse, et double en-tête rejeté
curl -sk -o /dev/null -w "%{http_code}\n" -H "x-api-key: $K" $B/1                      # 200
curl -sk -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $K" -H "X-Api-Key: $K" $B/1   # 403

# V-01 — clé de participant inventée, acceptée
curl -sk -w "\n[%{http_code}]\n" -H "Content-Type: application/json" -H "X-Api-Key: $K" \
  -X POST -d '{"cleParticipant":"jinvente-nimporte-quoi","QR":{"1":"a","2":"c","3":"b","4":"d"}}' \
  $B/1/reponses                                                       # 200

# A-03 — mêmes réponses, ordre des clés inversé
curl -sk -o /dev/null -w "%{http_code}\n" -H "Content-Type: application/json" -H "X-Api-Key: $K" \
  -X POST -d '{"cleParticipant":"zz","QR":{"4":"d","3":"b","2":"c","1":"a"}}' $B/1/reponses   # 404

# V-02 — 12 participants distincts, en parallèle réel depuis un seul processus curl
#         (12 requêtes séparées par --next, puis --parallel --parallel-immediate --parallel-max 12)
# Résultat observé : 200 500 500 500 500 500 500 500 500 500 500 200
#                    2 réponses sur 12 enregistrées, 9 participants marqués comme ayant répondu
#
# Le même essai avec 25 processus curl distincts donne 1 x 200 et 24 x 409, sans aucune erreur :
# le démarrage échelonné des processus sérialise les requêtes et masque complètement le défaut.
```

Les fichiers `Data/participants.json` et `Data/reponsesRecues.json` ont été remis dans leur état
versionné (`git checkout`) après les essais. Seuls `bin/` et `obj/` restent modifiés dans l'arbre de
travail — ce qui est en soi la démonstration de **A-01**.
