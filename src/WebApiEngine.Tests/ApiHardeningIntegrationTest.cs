using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BPMN.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class ApiHardeningIntegrationTest
{
    [Test]
    public async Task Health_ShouldReturnSuccessfulLivenessPayload()
    {
        var storage = new TestStorage();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<HealthStatusDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Status.Should().Be("Healthy");
        payload.Result.Storage.Should().Be("NotChecked");
    }

    [Test]
    public async Task ReadyHealth_ShouldReturnServiceUnavailable_WhenStorageProbeFails()
    {
        var storage = new TestStorage
        {
            ThrowOnGetAllDefinitions = true
        };

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<HealthStatusDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.Result.Should().NotBeNull();
        payload.Result!.Status.Should().Be("Unhealthy");
        payload.Result.Storage.Should().Be("Unavailable");
        payload.ErrorMessage.Should().Be("Storage is unavailable.");
    }

    [Test]
    public async Task UploadDefinition_ShouldUseTechnicalUserHeader_WhenNoAuthenticationExistsYet()
    {
        var storage = new TestStorage();
        var expectedUserId = Guid.NewGuid();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HttpContextCurrentUserContextAccessor.UserIdHeaderName, expectedUserId.ToString());

        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">
                    <bpmn:process id="Process_Invoice" isExecutable="true" />
                  </bpmn:definitions>
                  """;

        var response = await client.PostAsync("/definition", new StringContent(xml));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BpmnDefinitionDto>();
        payload.Should().NotBeNull();
        payload!.SavedByUser.Should().Be(expectedUserId);
        payload.Hash.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))));
        storage.StoredDefinitions.Should().ContainSingle();
        storage.StoredDefinitions[0].SavedByUser.Should().Be(expectedUserId);
    }

    [Test]
    public async Task GetAllUserTasks_ShouldUseFallbackUserId_WhenNoAuthenticationDataIsPresent()
    {
        var storage = new TestStorage();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.LastRequestedExtendedUserTaskUserId.Should().Be(HttpContextCurrentUserContextAccessor.FallbackUserId);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result.Should().BeEmpty();
    }

    [Test]
    public async Task MissingFormMetadata_ShouldReturnJsonApiContract_WhenControllerThrowsFileNotFound()
    {
        var storage = new TestStorage
        {
            ThrowOnGetFormMetadata = true
        };

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/form/meta/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Form metadata was not found");
    }

    [Test]
    public async Task MessageEndpoint_ShouldReturnApiStatusResult_WhenNoSubscriptionMatches()
    {
        var storage = new TestStorage();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/message", new MessageDto
        {
            Name = "InvoiceReceived",
            CorrelationKey = "INV-1000"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<string>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("No process instance is waiting for a message");
    }

    [Test]
    public async Task MessageEndpoint_ShouldReturnSuccessfulResultPayload_WhenMessageWasHandled()
    {
        var storage = new TestStorage();
        var definitionId = Guid.NewGuid();
        const string correlationKey = "INV-1000";
        storage.StoredDefinitions.Add(new BpmnDefinition
        {
            Id = definitionId,
            DefinitionId = "invoice-process",
            Hash = "hash",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true
        });
        storage.StoredBinaries[definitionId] = """
                                              <?xml version="1.0" encoding="UTF-8"?>
                                              <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                                                id="Definitions_1"
                                                                targetNamespace="http://bpmn.io/schema/bpmn">
                                                <bpmn:message id="Message_InvoiceReceived" name="InvoiceReceived" />
                                                <bpmn:process id="Process_Invoice" isExecutable="true">
                                                  <bpmn:startEvent id="StartEvent_1">
                                                    <bpmn:outgoing>Flow_1</bpmn:outgoing>
                                                    <bpmn:messageEventDefinition id="MessageEventDefinition_1" messageRef="Message_InvoiceReceived" />
                                                  </bpmn:startEvent>
                                                  <bpmn:endEvent id="EndEvent_1">
                                                    <bpmn:incoming>Flow_1</bpmn:incoming>
                                                  </bpmn:endEvent>
                                                  <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="EndEvent_1" />
                                                </bpmn:process>
                                              </bpmn:definitions>
                                              """;
        storage.MessageSubscriptions.Add(new MessageSubscription(
            new MessageDefinition
            {
                Name = "InvoiceReceived",
                FlowzerCorrelationKey = correlationKey
            },
            "Process_Invoice",
            "invoice-process",
            definitionId));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/message", new MessageDto
        {
            Name = "InvoiceReceived",
            CorrelationKey = correlationKey
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<string>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().Contain("InvoiceReceived");
        payload.Result.Should().Contain(correlationKey);
        payload.ErrorMessage.Should().BeNull();
    }

    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
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

    private sealed class TestStorage : ITransactionalStorage
    {
        public bool ThrowOnGetAllDefinitions { get; set; }
        public bool ThrowOnGetFormMetadata { get; set; }
        public Guid? LastRequestedExtendedUserTaskUserId { get; set; }
        public List<BpmnDefinition> StoredDefinitions { get; } = [];
        public Dictionary<Guid, string> StoredBinaries { get; } = [];
        public List<MessageSubscription> MessageSubscriptions { get; } = [];

        public IDefinitionStorage DefinitionStorage => new TestDefinitionStorage(this);
        public IMessageSubscriptionStorage SubscriptionStorage => new TestSubscriptionStorage(this);
        public IInstanceStorage InstanceStorage { get; } = new TestInstanceStorage();
        public IFormStorage FormStorage => new TestFormStorage(this);

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

    private sealed class TestDefinitionStorage(TestStorage storage) : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data)
        {
            storage.StoredBinaries[guid] = data;
            return Task.CompletedTask;
        }

        public Task<string> GetBinary(Guid guid)
        {
            if (storage.StoredBinaries.TryGetValue(guid, out var data))
            {
                return Task.FromResult(data);
            }

            throw new FileNotFoundException($"Binary definition {guid} was not found.");
        }

        public Task<Guid[]> GetAllBinaryDefinitions()
        {
            return Task.FromResult(storage.StoredDefinitions.Select(definition => definition.Id).ToArray());
        }

        public Task<BpmnDefinition[]> GetAllDefinitions()
        {
            if (storage.ThrowOnGetAllDefinitions)
            {
                throw new IOException("Definitions directory is currently unavailable.");
            }

            return Task.FromResult(storage.StoredDefinitions.ToArray());
        }

        public Task StoreDefinition(BpmnDefinition definition)
        {
            storage.StoredDefinitions.RemoveAll(existing => existing.Id == definition.Id);
            storage.StoredDefinitions.Add(definition);
            return Task.CompletedTask;
        }

        public Task<Model.Version?> GetMaxVersionId(string modelId)
        {
            var version = storage.StoredDefinitions
                .Where(definition => definition.DefinitionId == modelId)
                .OrderByDescending(definition => definition.Version)
                .Select(definition => definition.Version)
                .Cast<Model.Version?>()
                .FirstOrDefault();

            return Task.FromResult(version);
        }

        public Task<BpmnDefinition> GetDefinitionById(Guid id)
        {
            var definition = storage.StoredDefinitions.SingleOrDefault(existing => existing.Id == id)
                             ?? throw new FileNotFoundException($"Definition {id} was not found.");

            return Task.FromResult(definition);
        }

        public Task<BpmnDefinition> GetLatestDefinition(string definitionId)
        {
            var definition = storage.StoredDefinitions
                .Where(existing => existing.DefinitionId == definitionId)
                .OrderByDescending(existing => existing.Version)
                .FirstOrDefault()
                             ?? throw new FileNotFoundException($"Latest definition for {definitionId} was not found.");

            return Task.FromResult(definition);
        }

        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
        {
            var definition = storage.StoredDefinitions
                .SingleOrDefault(existing => existing.DefinitionId == definitionDefinitionId && existing.IsActive);

            return Task.FromResult(definition);
        }

        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
        {
            return Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        }

        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            return Task.CompletedTask;
        }

        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            return Task.CompletedTask;
        }

        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
        {
            throw new FileNotFoundException($"Meta definition {id} was not found.");
        }
    }

    private sealed class TestSubscriptionStorage(TestStorage storage) : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() =>
            Task.FromResult(storage.MessageSubscriptions.AsEnumerable());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) =>
            Task.FromResult(storage.MessageSubscriptions
                .Where(subscription =>
                    subscription.Message.Name == messageName &&
                    subscription.Message.FlowzerCorrelationKey == correlationKey &&
                    subscription.ProcessInstanceId == messageInstanceId));

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) =>
            Task.FromResult(storage.MessageSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId));

        public Task AddMessageSubscription(MessageSubscription messageSubscription)
        {
            storage.MessageSubscriptions.Add(messageSubscription);
            return Task.CompletedTask;
        }

        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            storage.MessageSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId)
        {
            storage.MessageSubscriptions.RemoveAll(subscription =>
                subscription.RelatedDefinitionId == metaDefinitionId && subscription.ProcessInstanceId == null);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;

        public void AddSignalSubscription(SignalSubscription signalSubscription)
        {
        }

        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<SignalSubscription>());

        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
        {
        }

        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<UserTaskSubscription>());

        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
        {
            storage.LastRequestedExtendedUserTaskUserId = userId;
            return Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        }

        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;

        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;

        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
        {
        }

        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class TestInstanceStorage : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            throw new FileNotFoundException($"Process instance {processInstanceId} was not found.");
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
        {
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances()
        {
            return Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances()
        {
            return Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        }
    }

    private sealed class TestFormStorage(TestStorage storage) : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => Task.CompletedTask;

        public Task<FormMetadata> GetFormMetaData(Guid formId)
        {
            if (storage.ThrowOnGetFormMetadata)
            {
                throw new FileNotFoundException($"Form metadata was not found for {formId}.");
            }

            throw new NotImplementedException();
        }

        public Task<IEnumerable<FormMetadata>> GetFormMetadatas()
        {
            return Task.FromResult(Enumerable.Empty<FormMetadata>());
        }

        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;

        public Task DeleteFormMetaData(Guid formId) => Task.CompletedTask;

        public Task SaveForm(Form form) => Task.CompletedTask;

        public Task<Form> GetForm(Guid id)
        {
            throw new FileNotFoundException($"Form {id} was not found.");
        }

        public Task<IEnumerable<Form>> GetForms(Guid formId)
        {
            return Task.FromResult(Enumerable.Empty<Form>());
        }

        public Task DeleteForm(Guid id) => Task.CompletedTask;

        public Task<Model.Version> GetMaxVersion(Guid formId)
        {
            return Task.FromResult(new Model.Version());
        }
    }
}
