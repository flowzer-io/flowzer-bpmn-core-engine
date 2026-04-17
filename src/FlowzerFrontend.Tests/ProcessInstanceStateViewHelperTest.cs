using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class ProcessInstanceStateViewHelperTest
{
    // Testzweck: Prüft, dass aktive und laufende Runtime-Zustände lesbare UI-Labels erhalten.
    [TestCase(ProcessInstanceStateDto.Initialized, "Initialized")]
    [TestCase(ProcessInstanceStateDto.Running, "Running")]
    [TestCase(ProcessInstanceStateDto.Waiting, "Waiting")]
    public void GetLabel_ShouldReturnReadableLabels(ProcessInstanceStateDto state, string expectedLabel)
    {
        var result = ProcessInstanceStateViewHelper.GetLabel(state);

        result.Should().Be(expectedLabel);
    }

    // Testzweck: Prüft, dass abgeschlossene Zustände in der UI dieselbe Erfolgs-Tonklasse verwenden.
    [TestCase(ProcessInstanceStateDto.Completed)]
    [TestCase(ProcessInstanceStateDto.Terminated)]
    [TestCase(ProcessInstanceStateDto.Compensated)]
    public void GetToneClass_ShouldReturnSuccessTone_ForFinishedStates(ProcessInstanceStateDto state)
    {
        var result = ProcessInstanceStateViewHelper.GetToneClass(state);

        result.Should().Be("status-pill-success");
    }

    // Testzweck: Prüft, dass Fehlerzustände in der UI eindeutig als kritisch markiert werden.
    [TestCase(ProcessInstanceStateDto.Failing)]
    [TestCase(ProcessInstanceStateDto.Failed)]
    public void GetToneClass_ShouldReturnDangerTone_ForErrorStates(ProcessInstanceStateDto state)
    {
        var result = ProcessInstanceStateViewHelper.GetToneClass(state);

        result.Should().Be("status-pill-danger");
    }
}
