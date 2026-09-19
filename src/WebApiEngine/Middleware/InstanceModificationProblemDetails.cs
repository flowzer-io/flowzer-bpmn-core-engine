using WebApiEngine.Shared;

namespace WebApiEngine.Middleware;

/// <summary>
/// Oeffentlicher Problem-Details-Vertrag fuer einen abgelehnten Instanzeingriff. Wie bei den
/// BPMN-Modellfehlern eine eigene Klasse statt freier
/// <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions"/>: Nur so erscheinen die
/// Befunde im OpenAPI-Schema und damit in generierten Clients.
///
/// Der Trockenlauf beantwortet dieselbe Frage mit 200 und
/// <see cref="InstanceModificationPreviewDto.Problems"/>. Hier landet nur, wer trotzdem
/// ausfuehrt — etwa weil sich die Instanz seit dem Trockenlauf bewegt hat.
/// </summary>
public sealed class InstanceModificationProblemDetails : ApiValidationProblem
{
    /// <summary>Die Hindernisse mit denselben stabilen Codes wie im Trockenlauf.</summary>
    public IReadOnlyList<InstanceModificationFindingDto> Problems { get; init; } = [];
}
