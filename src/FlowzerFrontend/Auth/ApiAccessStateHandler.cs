using System.Net;

namespace FlowzerFrontend.Auth;

/// <summary>
/// Liest die Antworten der Flowzer-API mit und pflegt daraus den <see cref="ApiAccessState"/>.
///
/// Eine 403 kann zweierlei heissen: Dieses Konto darf Flowzer gar nicht benutzen, oder es darf
/// nur diese eine Handlung nicht. Die API unterscheidet das im Header
/// <c>X-Flowzer-Access-Denied</c>; ohne den Header wird konservativ vom fehlenden Zugang
/// ausgegangen. 401 bleibt bewusst unberuehrt, weil das die Anmeldung selbst betrifft.
/// </summary>
public sealed class ApiAccessStateHandler(ApiAccessState accessState) : DelegatingHandler
{
    public const string AccessDeniedHeader = "X-Flowzer-Access-Denied";
    public const string DeniedCapability = "capability";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Forbidden when IsCapabilityDenial(response):
                // Wer eine einzelne Handlung nicht darf, ist offensichtlich freigeschaltet.
                accessState.MarkAccessGranted();
                break;
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

    private static bool IsCapabilityDenial(HttpResponseMessage response) =>
        response.Headers.TryGetValues(AccessDeniedHeader, out var values)
        && values.Any(value => string.Equals(value, DeniedCapability, StringComparison.OrdinalIgnoreCase));
}
