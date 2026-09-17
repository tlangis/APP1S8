using Xunit;

// ServiceSondage lit et écrit ses fichiers avec des chemins relatifs ("Data/sondage.txt", ...)
// résolus contre le répertoire courant du processus. Les tests redirigent donc le répertoire
// courant vers un dossier temporaire, ce qui est un état global : toute exécution parallèle
// ferait interférer les tests entre eux.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
