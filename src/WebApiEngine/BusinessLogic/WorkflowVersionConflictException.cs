namespace WebApiEngine.BusinessLogic;

/// <summary>Das angezeigte Startformular gehört nicht mehr zur ausgewählten deployten Fassung.</summary>
public sealed class WorkflowVersionConflictException() : Exception(
    "Der Workflow wurde inzwischen geändert. Bitte das Startformular neu öffnen; es wurde nichts gestartet.");
