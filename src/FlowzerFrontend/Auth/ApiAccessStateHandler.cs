using System.Net;

namespace FlowzerFrontend.Auth;

/// <summary>
/// Liest die Antworten der Flowzer-API mit und pflegt daraus den <see cref="ApiAccessState"/>.
/// 403 heisst: angemeldet, aber ohne die verlangte Rolle. 401 bleibt bewusst unberuehrt, weil
/// das die Anmeldung selbst betrifft und die Bibliothek dafuer schon einen eigenen Weg hat.
/// </summary>
public sealed class ApiAccessStateHandler(ApiAccessState accessState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                accessState.MarkAccessDenied();
                break;
            case HttpStatusCode.Unauthorized:
                break;
            default:
                if (response.IsSuccessStatusCode)
                {
                    // Eine nachtraeglich vergebene Rolle soll ohne Neuladen wirken.
                    accessState.MarkAccessGranted();
                }

                break;
        }

        return response;
    }
}
