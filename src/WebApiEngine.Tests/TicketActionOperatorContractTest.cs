using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApiEngine.Auth;
using WebApiEngine.Controller;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public sealed class TicketActionOperatorContractTest
{
    // Testzweck: Ein persönlicher Proof bleibt beim Betrieb, wird nicht gecacht und nimmt keine fremde Benutzer-/Rollenwahl an.
    [Test]
    public void OperatorProof_ShouldKeepExactOperatorReadOnlyRouteAndNoStore()
    {
        var type = typeof(TicketActionOperatorAccessController);
        type.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(FlowzerPolicies.Operator);
        type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        var method = type.GetMethod(nameof(TicketActionOperatorAccessController.Check))!;
        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{instanceId:guid}/ticket-action-operator-access");
        method.GetParameters().Select(x => x.ParameterType).Should().Equal(typeof(Guid), typeof(CancellationToken));
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
    }

    // Testzweck: OpenAPI muss alle neun gebundenen Felder verlangen; Proof enthält weder Formulare/Variablen noch Rollen/Secrets.
    [Test]
    public void OperatorProof_ShouldExposeExactlyRequiredBindingFields()
    {
        var dto = new TicketActionOperatorAccessDto(Guid.NewGuid(), "definition", Guid.NewGuid(), "https://issuer.test", "initiator",
            "https://issuer.test", "operator", "tt-exchange", DateTimeOffset.UnixEpoch);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.EnumerateObject().Select(x => x.Name).Should().Equal("processInstanceId", "metaDefinitionId", "definitionId",
            "initiatorIssuer", "initiatorSubject", "actorIssuer", "actorSubject", "actorAuthorizedClientId", "checkedAtUtc");
        typeof(TicketActionOperatorAccessDto).GetProperties().Should().HaveCount(9);
        typeof(TicketActionOperatorAccessDto).GetProperties().Should().OnlyContain(x => x.GetCustomAttribute<RequiredAttribute>() != null);
    }
}
