namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Expliziter fachlicher Endzustandskonflikt. Technische InvalidOperationExceptions
/// aus Engine, Ablage oder Projektion dürfen niemals als abgeschlossener Rückzug gelten.
/// Der historische Betriebsabbruch kann den InvalidOperationException-Basisvertrag behalten.
/// </summary>
public sealed class InstanceFinishedConflictException(string message) : InvalidOperationException(message);
