using FluentAssertions;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class LegacyFormBindingUpgradeTest
{
    // Testzweck: Ein Update bindet eine eindeutige Altversion automatisch; weitere
    // Veröffentlichungen oder ein wiederholtes Update verändern laufende Vorgänge nicht.
    [Test]
    public async Task Upgrade_ShouldBindOnceAndPreserveOriginalSchema()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var (definition, form) = await Seed(context);
        (await LegacyFormBindingUpgrade.ApplyAsync(context.Storage)).Should().Be(1);
        var bound = (await new FormKeyResolver(context.Storage).ResolveAsync("Approval", definition.Id)).Form!;
        bound.Id.Should().Be(form.Id);
        bound.FormData.Should().Be(form.FormData);
        await SaveVersion(context, form.FormId, 2);
        (await LegacyFormBindingUpgrade.ApplyAsync(context.Storage)).Should().Be(0);
        (await new FormKeyResolver(context.Storage).ResolveAsync("Approval", definition.Id)).Form!.Id.Should().Be(form.Id);
        (await context.Storage.FormStorage.GetForm(form.Id)).FormData.Should().Be(form.FormData);
    }

    // Testzweck: Bei mehreren möglichen Altversionen wird vor dem ersten Schreibvorgang
    // abgebrochen. Ein Update darf weder „latest“ erraten noch einen halben Bestand ändern.
    [Test]
    public async Task Upgrade_ShouldPreflightAllDefinitionsBeforeWriting()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var (definition, form) = await Seed(context);
        await SaveVersion(context, form.FormId, 2);
        Func<Task> upgrade = () => LegacyFormBindingUpgrade.ApplyAsync(context.Storage);
        await upgrade.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Update*Approval*");
        (await context.Storage.DefinitionStorage.GetDefinitionById(definition.Id)).FormBindings.Should().BeNull();
    }

    // Testzweck: Eine explizite Versionsreferenz bleibt auch bei mehreren vorhandenen
    // Veröffentlichungen eindeutig und kann ohne Benutzerdialog übernommen werden.
    [Test]
    public async Task Upgrade_ShouldHonorExplicitVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var (definition, form) = await Seed(context, "Approval:1.0");
        await SaveVersion(context, form.FormId, 2);
        await LegacyFormBindingUpgrade.ApplyAsync(context.Storage);
        (await new FormKeyResolver(context.Storage).ResolveAsync("Approval:1.0", definition.Id)).Form!.Id.Should().Be(form.Id);
    }

    private static async Task<(BpmnDefinition, Form)> Seed(AuthenticatedWorkflowTestContext context, string key = "Approval")
    {
        var formId = Guid.NewGuid();
        await context.Storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Approval" });
        var form = await SaveVersion(context, formId, 1);
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = "Legacy", Hash = "test", SavedByUser = Guid.NewGuid(),
            Version = new Model.Version(1, 0), IsActive = true
        };
        await context.Storage.DefinitionStorage.StoreDefinition(definition);
        await context.Storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:zeebe="http://camunda.org/schema/zeebe/1.0">
              <bpmn:process id="Process"><bpmn:startEvent id="Start"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="{{key}}" />
              </bpmn:extensionElements></bpmn:startEvent></bpmn:process>
            </bpmn:definitions>
            """);
        return (definition, form);
    }

    private static async Task<Form> SaveVersion(AuthenticatedWorkflowTestContext context, Guid formId, int major)
    {
        var form = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(major, 0),
            FormData = """{"components":[{"type":"textfield","key":"reason","input":true}]}""" };
        await context.Storage.FormStorage.SaveForm(form);
        return form;
    }
}
