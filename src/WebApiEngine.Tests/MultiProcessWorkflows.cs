namespace WebApiEngine.Tests;

/// <summary>
/// Die kleinen Workflows der Mehrprozesstests. Jeder deckt genau einen Wartezustand ab,
/// damit eine gebrochene Invariante eindeutig einem Pfad zuzuordnen ist.
/// </summary>
internal static class MultiProcessWorkflows
{
    internal const string UserTaskDefinitionId = "Definitions_MpUserTask";
    internal const string ServiceTaskDefinitionId = "Definitions_MpServiceTask";
    internal const string MessageCatchDefinitionId = "Definitions_MpMessageCatch";
    internal const string MessageStartDefinitionId = "Definitions_MpMessageStart";
    internal const string TimerStartDefinitionId = "Definitions_MpTimerStart";
    internal const string TimerCatchDefinitionId = "Definitions_MpTimerCatch";

    internal const string ServiceTaskType = "mp-service";
    internal const string CatchMessageName = "MpContinue";
    internal const string CatchCorrelationKey = "mp-correlation";
    internal const string StartMessageName = "MpStart";

    /// <summary>Start → menschliche Aufgabe → Ende. <paramref name="taskScheduleXml"/> bindet Termine.</summary>
    internal static string UserTask(string? taskScheduleXml = null) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="{{UserTaskDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpUserTask" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToReview</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToReview" sourceRef="Start" targetRef="Review" />
            <bpmn:userTask id="Review" name="Review">
              <bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
                {{taskScheduleXml ?? string.Empty}}
              </bpmn:extensionElements>
              <bpmn:incoming>ToReview</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Review" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Start → Service-Task fuer einen externen Worker → Ende.</summary>
    internal static string ServiceTask() => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="{{ServiceTaskDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpServiceTask" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToWork</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToWork" sourceRef="Start" targetRef="Work" />
            <bpmn:serviceTask id="Work" name="Work">
              <bpmn:extensionElements><zeebe:taskDefinition type="{{ServiceTaskType}}" /></bpmn:extensionElements>
              <bpmn:incoming>ToWork</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Work" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Start → wartendes Nachrichten-Catch-Event mit festem Korrelationsschluessel → Ende.</summary>
    internal static string MessageCatch() => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="{{MessageCatchDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpMessageCatch" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToWait</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToWait" sourceRef="Start" targetRef="Wait" />
            <bpmn:intermediateCatchEvent id="Wait">
              <bpmn:incoming>ToWait</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
              <bpmn:messageEventDefinition messageRef="Message_MpContinue" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Wait" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
          <bpmn:message id="Message_MpContinue" name="{{CatchMessageName}}">
            <bpmn:extensionElements><zeebe:subscription correlationKey="{{CatchCorrelationKey}}" /></bpmn:extensionElements>
          </bpmn:message>
        </bpmn:definitions>
        """;

    /// <summary>Nachrichten-Startereignis ohne Korrelationsschluessel.</summary>
    internal static string MessageStart() => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            id="{{MessageStartDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpMessageStart" isExecutable="true">
            <bpmn:startEvent id="Start">
              <bpmn:outgoing>ToEnd</bpmn:outgoing>
              <bpmn:messageEventDefinition messageRef="Message_MpStart" />
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Start" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
          <bpmn:message id="Message_MpStart" name="{{StartMessageName}}" />
        </bpmn:definitions>
        """;

    /// <summary>Einmaliger Timer-Start; er darf hoechstens eine Instanz je Faelligkeit erzeugen.</summary>
    internal static string TimerStart() => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            id="{{TimerStartDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpTimerStart" isExecutable="true">
            <bpmn:startEvent id="StartTimer">
              <bpmn:outgoing>ToEnd</bpmn:outgoing>
              <bpmn:timerEventDefinition id="TimerDefinition_MpStart">
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="StartTimer" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Start → wartender Intermediate-Timer → Ende.</summary>
    internal static string TimerCatch() => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            id="{{TimerCatchDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_MpTimerCatch" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToWait</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToWait" sourceRef="Start" targetRef="Wait" />
            <bpmn:intermediateCatchEvent id="Wait">
              <bpmn:incoming>ToWait</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
              <bpmn:timerEventDefinition id="TimerDefinition_MpCatch">
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Wait" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
