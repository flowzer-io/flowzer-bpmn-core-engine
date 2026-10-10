using System.Dynamic;
using FluentAssertions;
using Newtonsoft.Json;
using StorageSystem;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class StorageVariableDataConverterTest
{
    // Testzweck: Echte dynamische Daten speichern einen strikten SubjectRef ohne generierte
    // CLR-Metadaten und lesen ihn mit exakt denselben zwei Datenfeldern zurück.
    [Test]
    public void ProcessValues_ShouldRoundTripAsPlainJsonWithoutTypeMetadata()
    {
        var id = Guid.NewGuid();
        var data = new ExpandoObject();
        ((IDictionary<string, object?>)data)["selectedUser"] = DirectorySubjectValue.Normalize(new Model.SubjectRef(Model.DirectorySubjectKind.User, id));
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, Converters = [new StorageVariableDataConverter()] };
        var json = JsonConvert.SerializeObject(data, settings);
        json.Should().NotContain("$type");
        var read = JsonConvert.DeserializeObject<ExpandoObject>(json, settings)!;
        DirectorySubjectValue.TryParse(((IDictionary<string, object?>)read)["selectedUser"], out var subject).Should().BeTrue();
        subject.Id.Should().Be(id);
    }

    // Testzweck: Vom Browser/Worker eingereichte Typfelder bleiben Daten, werden weder
    // instanziiert noch als angebliche interne Metadaten entfernt oder fachlich akzeptiert.
    [Test]
    public void SubmittedTypeMetadata_ShouldRemainLiteralAndFailSubjectValidation()
    {
        var json = "{\"selectedUser\":{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",\"kind\":\"user\",\"id\":\"11000000-0000-4000-8000-000000000001\"}}";
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, Converters = [new StorageVariableDataConverter()] };
        var read = JsonConvert.DeserializeObject<ExpandoObject>(json, settings)!;
        var selected = ((IDictionary<string, object?>)read)["selectedUser"];
        selected.Should().BeOfType<ExpandoObject>();
        ((IDictionary<string, object?>)selected!).Should().ContainKey("$type");
        DirectorySubjectValue.TryParse(selected, out _).Should().BeFalse();
        JsonConvert.SerializeObject(read, settings).Should().Contain("$type");
    }
}
