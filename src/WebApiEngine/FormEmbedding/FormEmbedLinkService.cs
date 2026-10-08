using System.Dynamic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;
using WebApiEngine.Shared;

namespace WebApiEngine.FormEmbedding;

/// <summary>
/// Einmalige Anzeige-Freigaben, keine zweite Anmeldung und keine Arbeitssitzung.
/// Mutation und Directory bleiben ausschließlich an den authentifizierten Host gebunden.
/// </summary>
public sealed class FormEmbedLinkService(
    BpmnBusinessLogic engine,
    IStorageSystem readStorage,
    ICurrentUserContextAccessor currentUser,
    IOptions<FormEmbeddingOptions> options,
    TimeProvider clock)
{
    /// <summary>Erstellt nur für eine persönlich berechtigte Directory-Identität einen Einstieg.</summary>
    public async Task<FormEmbedLinkDto?> IssueAsync(Guid taskId, string hostOrigin, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Allows(hostOrigin)) return null;
        var actor = currentUser.GetCurrentUser();
        actor.RequireResolvedUserId("opening embedded forms");
        if (actor.Identity is null) return null;
        // Niemals globale Ablaufbereinigung unter einer Aufgaben-/Engine-Sperre.
        await readStorage.FormEmbedGrantStorage.CleanupExpired(clock.GetUtcNow(), cancellationToken);
        return await engine.ExecuteUserTaskMutationAsync<FormEmbedLinkDto?>(async storage =>
        {
            var authorized = await FindTask(storage, taskId, actor);
            if (authorized is null) return null;
            var task = authorized.Value.Task;
            if (await ResolveForm(storage, task) is null) return null;
            var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var now = clock.GetUtcNow();
            var expires = now.AddMinutes(5);
            await storage.FormEmbedGrantStorage.Create(new FormEmbedGrant
            {
                SecretHash = Hash(secret), UserTaskId = task.Id, DefinitionId = task.DefinitionId,
                OwnerUserId = actor.UserId, Owner = actor.Identity, HostOrigin = hostOrigin,
                TaskRevision = authorized.Value.Access.State?.Revision ?? 0, ExpiresAtUtc = expires
            }, now);
            return new FormEmbedLinkDto
            {
                Url = options.Value.PublicOrigin + "/embed.html#" + secret, RedeemBeforeUtc = expires
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Verbraucht das Secret und liest den gebundenen Snapshot nach erneuter Rechteprüfung.
    /// Nach der Antwort wird es weder zum Speichern noch zum Abschließen gebraucht.
    /// </summary>
    public async Task<FormEmbedSnapshotDto?> RedeemAsync(string? secret, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || secret is null || secret.Length != 43
            || secret.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            return null;
        var hash = Hash(secret);
        // Zufällige anonyme Secrets dürfen weder die globale Engine-Sperre noch
        // Schreibtransaktionen belegen. Die Vorprüfung selbst erteilt kein Recht.
        var candidate = await readStorage.FormEmbedGrantStorage.Find(hash, clock.GetUtcNow(), cancellationToken);
        if (candidate is null) return null;
        return await engine.ExecuteUserTaskMutationAsync<FormEmbedSnapshotDto?>(async storage =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Dieselbe Reihenfolge wie Abschluss/Abbruch: Aufgabe VOR Freigabe.
            // Andernfalls wartet DELETE CASCADE auf die Freigabe und diese auf die Aufgabe.
            if (!await storage.UserTaskLifecycleStorage.LockTask(candidate.UserTaskId)) return null;
            var grant = await storage.FormEmbedGrantStorage.TryTake(hash, clock.GetUtcNow());
            if (grant is null || !options.Value.Allows(grant.HostOrigin)) return null;
            // Gespeichert sind ausschließlich stabile Identitäten, keine veralteten Gruppenclaims
            // und keine Operatorrolle. Der aktuelle Directory-Stand entscheidet erneut.
            var actor = new CurrentUserContext(grant.OwnerUserId, "form-embed-read", false) { Identity = grant.Owner };
            var authorized = await FindTask(storage, grant.UserTaskId, actor);
            if (authorized is null || authorized.Value.Task.DefinitionId != grant.DefinitionId
                || (authorized.Value.Access.State?.Revision ?? 0) != grant.TaskRevision) return null;
            var task = authorized.Value.Task;
            var form = await ResolveForm(storage, task);
            if (form is null) return null;
            var instance = await storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
            var token = instance.Tokens.Single(t => t.Id == task.Token.Id);
            var values = FormContextProjection.Project(form.FormData, TaskFormContext.Read(instance.Tokens, token));
            var draft = await storage.UserTaskDraftStorage.Get(task.Id, UserTaskDraftOwnerKey.Create(actor));
            if (draft is not null && (draft.DefinitionId != task.DefinitionId || draft.TokenId != token.Id
                || draft.ProcessInstanceId != instance.InstanceId)) return null;
            return new FormEmbedSnapshotDto
            {
                UserTaskId = task.Id, HostOrigin = grant.HostOrigin, TaskRevision = grant.TaskRevision,
                Form = form, Context = values,
                Draft = new UserTaskDraftDto
                {
                    UserTaskId = task.Id, Revision = draft?.Revision ?? 0, UpdatedAtUtc = draft?.UpdatedAtUtc,
                    Data = draft is null ? new ExpandoObject()
                        : Newtonsoft.Json.JsonConvert.DeserializeObject<ExpandoObject>(draft.DataJson)
                          ?? throw new InvalidDataException("Stored draft is empty.")
                }
            };
        }, cancellationToken);
    }

    private static async Task<FormDto?> ResolveForm(IStorageSystem storage, ExtendedUserTaskSubscription task)
    {
        var key = (task.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation;
        var form = (await new FormKeyResolver(storage).ResolveAsync(key, task.DefinitionId)).Form;
        if (form?.FormData is null) return null;
        // Auch historische Bindungen müssen den geschlossenen Formularvertrag erfüllen.
        _ = FormContractCompiler.Compile(form.FormData);
        return form;
    }

    private static async Task<(ExtendedUserTaskSubscription Task, UserTaskAccess Access)?> FindTask(
        IStorageSystem storage, Guid taskId, CurrentUserContext actor)
    {
        if (!await storage.UserTaskLifecycleStorage.LockTask(taskId)) return null;
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task?.ProcessInstanceId is not { } instanceId
            || task.Token is not { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask }) return null;
        UserTaskAssignment.EnsureAssignmentFromModel(task);
        // Der neue Embed-Pfad unterstützt absichtlich keine Namen-/Gruppenclaim-Fallbacks.
        // Anders als bestehende Legacy-APIs benötigt er stets aktuelle stabile Identitäten.
        if (task.AssignmentMode != BPMN.HumanInteraction.UserTaskAssignmentMode.Directory) return null;
        var snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot();
        if (DirectoryIdentityAccess.Resolve(actor, snapshot) is null) return null;
        var access = await UserTaskWorkAuthorization.EvaluateAsync(storage, task, actor, canOperate: false, snapshot);
        if (!access.CanWork) return null;
        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return null; }
        var tokens = instance.Tokens.Where(t => t.Id == task.Token.Id).Take(2).ToArray();
        return tokens.Length == 1 && tokens[0].State == FlowNodeState.Active
            && tokens[0].CurrentFlowNode?.Id == task.Token.CurrentFlowNode.Id
            && instance.DefinitionId == task.DefinitionId && instance.ProcessId == task.ProcessId
            && instance.metaDefinitionId == task.MetaDefinitionId ? (task, access) : null;
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
}
