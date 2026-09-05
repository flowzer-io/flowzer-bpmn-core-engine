namespace FlowzerFrontend.Auth;

/// <summary>
/// Merkt sich, ob die API den Zugriff mit 403 verweigert hat. Angemeldet zu sein und
/// zugelassen zu sein sind zwei verschiedene Dinge: Der Identity Provider stellt jeder
/// Person im Realm ein Token aus, die Anwendung verlangt zusaetzlich eine Rolle. Ohne
/// diesen Zustand sieht eine solche Person nur rohe Fehlermeldungen auf jeder Seite.
/// </summary>
public sealed class ApiAccessState
{
    public bool IsAccessDenied { get; private set; }

    /// <summary>Wird nur bei einem echten Wechsel ausgeloest, nicht bei jeder Antwort.</summary>
    public event Action? Changed;

    public void MarkAccessDenied() => Set(true);

    public void MarkAccessGranted() => Set(false);

    private void Set(bool denied)
    {
        if (IsAccessDenied == denied)
        {
            return;
        }

        IsAccessDenied = denied;
        Changed?.Invoke();
    }
}
