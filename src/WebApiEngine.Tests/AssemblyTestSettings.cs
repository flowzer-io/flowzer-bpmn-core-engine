using NUnit.Framework;

// Die Web-API-Integrationstests booten mehrfach komplette ASP.NET-Hosts mit
// test-spezifischen DI-Overrides. Serielle Ausführung verhindert flakey
// Überschneidungen zwischen parallelen Host-Initialisierungen im CI-Lauf.
[assembly: LevelOfParallelism(1)]
