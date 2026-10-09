using System.Net;
using FluentAssertions;

namespace WebApiEngine.Tests;

public partial class ApplicationRolesIntegrationTest
{
    // Testzweck: Der echte kryptografische Bearer-/Operatorpfad bleibt vor dem
    // minimalen Read-only-Proof zwingend; eine normale Installation ohne TT-Bindung antwortet geschlossen 503.
    [TestCase("anonymous", HttpStatusCode.Unauthorized)]
    [TestCase("access", HttpStatusCode.Forbidden)]
    [TestCase("operator", HttpStatusCode.ServiceUnavailable)]
    public async Task TicketActionOperatorProof_ShouldRequireRealOperatorPolicyAndBoundInstallation(string actor, HttpStatusCode expected)
    {
        await using var factory = CreateFactory(modelerRole: "modeler", operatorRole: "operator");
        using var client = factory.CreateClient();
        if (actor != "anonymous") client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: actor == "operator" ? ["operator"] : []));
        using var response = await client.GetAsync($"/instance/{Guid.NewGuid():D}/ticket-action-operator-access");
        response.StatusCode.Should().Be(expected);
        if (actor == "operator") (await response.Content.ReadAsStringAsync()).Should().NotContain("ActorSubject");
    }
}
