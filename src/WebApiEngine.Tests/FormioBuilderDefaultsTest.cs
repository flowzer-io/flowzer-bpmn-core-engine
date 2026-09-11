using FluentAssertions;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class FormioBuilderDefaultsTest
{
    // Testzweck: Form.io-Zahlenfelder tragen step=any bereits ohne fachliche
    // Einschränkung; diese neutrale Einstellung darf die Veröffentlichung nicht blockieren.
    [TestCase("number")]
    [TestCase("currency")]
    public void NumericDefaultStep_ShouldCompile(string type)
    {
        var schema = "{\"components\":[{\"type\":\"" + type + "\",\"key\":\"amount\",\"validate\":{\"step\":\"any\"}}]}";
        Action compile = () => FormContractCompiler.Compile(schema);
        compile.Should().NotThrow();
    }

    // Testzweck: Die Default-Ausnahme darf echte Schrittweiten, Integerregeln und
    // unbekannte Validierungen nicht stillschweigend als durchgesetzt ausgeben.
    [TestCase("number", "{\"step\":2}")]
    [TestCase("number", "{\"integer\":true}")]
    [TestCase("textfield", "{\"step\":\"any\"}")]
    [TestCase("number", "{\"custom\":\"valid = true;\"}")]
    public void UnsupportedRules_ShouldStillFail(string type, string validation)
    {
        var schema = "{\"components\":[{\"type\":\"" + type + "\",\"key\":\"amount\",\"validate\":" + validation + "}]}";
        Action compile = () => FormContractCompiler.Compile(schema);
        compile.Should().Throw<FormContractException>().WithMessage("*validation.unsupported*");
    }
}
