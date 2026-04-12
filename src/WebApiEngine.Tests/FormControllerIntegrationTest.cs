using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public class FormControllerIntegrationTest
{
    [Test]
    public async Task SaveForm_ShouldCreateInitialVersion_WhenNoVersionExists()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = formId,
            FormData = "{\"type\":\"form\"}",
            Version = new VersionDto()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Version.Major.Should().Be(0);
        payload.Result.Version.Minor.Should().Be(1);
        storage.FormStorageSeed.Forms.Should().ContainSingle(form =>
            form.FormId == formId &&
            form.Version.Major == 0 &&
            form.Version.Minor == 1);
    }

    [Test]
    public async Task SaveForm_ShouldReturnBadRequest_WhenFormIdIsEmpty()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = Guid.Empty,
            FormData = "{\"type\":\"form\"}",
            Version = new VersionDto()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Be("FormId is required");
    }

    [Test]
    public async Task GetForm_ShouldReturnSpecificVersion_WhenVersionIdentifierIsProvided()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        var oldForm = new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{\"version\":\"1.0\"}"
        };
        var newForm = new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 1),
            FormData = "{\"version\":\"1.1\"}"
        };

        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Invoice" });
        storage.FormStorageSeed.Forms.AddRange([oldForm, newForm]);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/form/{formId}/1.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Id.Should().Be(oldForm.Id);
        payload.Result.FormData.Should().Be(oldForm.FormData);
        payload.Result.Version.ToString().Should().Be("1.0");
    }

    [Test]
    public async Task GetForm_ShouldReturnBadRequest_WhenVersionIdentifierIsInvalid()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{\"version\":\"1.0\"}"
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/form/{formId}/invalid-version");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Version string must have two parts separated by a dot.");
    }

    [Test]
    public async Task SaveFormMetadata_ShouldUseRouteFormId_WhenBodyFormIdIsEmpty()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/form/meta/{formId}", new FormMetaDataDto
        {
            FormId = Guid.Empty,
            Name = "Invoice"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle(metadata =>
            metadata.FormId == formId &&
            metadata.Name == "Invoice");
    }

    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TimerScheduler:Enabled"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();

                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));
            });
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage()
        {
            return storage;
        }
    }

    private sealed class TestStorage(TestFormStorage formStorage) : ITransactionalStorage
    {
        public static TestStorage Create()
        {
            return new TestStorage(new TestFormStorage());
        }

        public TestFormStorage FormStorageSeed { get; } = formStorage;
        public IDefinitionStorage DefinitionStorage { get; } = new NoOpDefinitionStorage();
        public IMessageSubscriptionStorage SubscriptionStorage { get; } = new NoOpMessageSubscriptionStorage();
        public IInstanceStorage InstanceStorage { get; } = new NoOpInstanceStorage();
        public IFormStorage FormStorage { get; } = formStorage;

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestFormStorage : IFormStorage
    {
        public List<FormMetadata> FormMetadatas { get; } = [];
        public List<Form> Forms { get; } = [];

        public Task SaveFormMetaData(FormMetadata formMetadata)
        {
            var existing = FormMetadatas.SingleOrDefault(metadata => metadata.FormId == formMetadata.FormId);
            if (existing == null)
            {
                FormMetadatas.Add(formMetadata);
            }
            else
            {
                existing.Name = formMetadata.Name;
            }

            return Task.CompletedTask;
        }

        public Task<FormMetadata> GetFormMetaData(Guid formId)
        {
            var metadata = FormMetadatas.Single(metadata => metadata.FormId == formId);
            return Task.FromResult(metadata);
        }

        public Task<IEnumerable<FormMetadata>> GetFormMetadatas()
        {
            return Task.FromResult(FormMetadatas.AsEnumerable());
        }

        public Task UpdateFormMetaData(FormMetadata formMetaData)
        {
            return SaveFormMetaData(formMetaData);
        }

        public Task DeleteFormMetaData(Guid formId)
        {
            FormMetadatas.RemoveAll(metadata => metadata.FormId == formId);
            Forms.RemoveAll(form => form.FormId == formId);
            return Task.CompletedTask;
        }

        public Task SaveForm(Form form)
        {
            Forms.Add(form);
            return Task.CompletedTask;
        }

        public Task<Form> GetForm(Guid id)
        {
            return Task.FromResult(Forms.Single(form => form.Id == id));
        }

        public Task<IEnumerable<Form>> GetForms(Guid formId)
        {
            return Task.FromResult(Forms.Where(form => form.FormId == formId).AsEnumerable());
        }

        public Task DeleteForm(Guid id)
        {
            Forms.RemoveAll(form => form.Id == id);
            return Task.CompletedTask;
        }

        public Task<Model.Version> GetMaxVersion(Guid formId)
        {
            var version = Forms
                .Where(form => form.FormId == formId)
                .OrderByDescending(form => form.Version)
                .Select(form => form.Version)
                .FirstOrDefault() ?? new Model.Version();

            return Task.FromResult(version);
        }
    }

    private sealed class NoOpDefinitionStorage : IDefinitionStorage
    {
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => Task.CompletedTask;
        public Task StoreBinary(Guid guid, string data) => Task.CompletedTask;
        public Task<Model.Version?> GetMaxVersionId(string modelId) => Task.FromResult<Model.Version?>(null);
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => Task.FromResult<BpmnDefinition?>(null);
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class NoOpMessageSubscriptionStorage : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) { }
        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());
        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) { }
        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) => Task.FromResult(Enumerable.Empty<UserTaskSubscription>());
        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) => Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class NoOpInstanceStorage : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => throw new NotSupportedException();
        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstance) => Task.CompletedTask;
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
    }
}
