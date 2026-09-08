using FluentAssertions;
using Model;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Additiver API-Vertrag fuer Legacy-Text und stabile Directory-Referenzen.</summary>
public sealed class FolderAssignmentMappingTest
{
    // Testzweck: Bestandsclients ohne referenceMode oder subjectRef muessen ihre bisherige
    // Freitextzuweisung unveraendert lesen, schreiben und auswerten koennen.
    [Test]
    public void TextAssignment_ShouldRemainTheCompatibleDefault()
    {
        var model = new FolderAssignmentDto
        {
            SubjectKind = "group",
            Subject = "/team/review",
            Role = "editor"
        }.ToModel();

        model.AssignmentMode.Should().Be(FolderAssignmentMode.Text);
        model.Subject.Should().Be("/team/review");
        model.DirectorySubject.Should().BeNull();
        model.ToDto().ReferenceMode.Should().Be("text");
    }

    // Testzweck: Eine Directory-Zuweisung behaelt Art und stabile UUID im Modell und in der
    // additiven SubjectRef-Projektion, ohne daraus einen Anzeigenamen abzuleiten.
    [Test]
    public void DirectoryAssignment_ShouldRoundTripAsTypedReference()
    {
        var id = Guid.NewGuid();
        var model = new FolderAssignmentDto
        {
            ReferenceMode = "directory",
            SubjectKind = "user",
            Subject = id.ToString(),
            SubjectRef = new SubjectRefDto { Kind = "user", Id = id },
            Role = "steward",
            DisplayName = "nicht vertrauenswuerdig"
        }.ToModel();

        model.AssignmentMode.Should().Be(FolderAssignmentMode.Directory);
        model.DirectorySubject.Should().Be(new SubjectRef(DirectorySubjectKind.User, id));
        model.ToDto().Should().BeEquivalentTo(new FolderAssignmentDto
        {
            ReferenceMode = "directory",
            SubjectKind = "user",
            Subject = id.ToString(),
            SubjectRef = new SubjectRefDto { Kind = "user", Id = id },
            Role = "steward",
            DisplayName = "nicht vertrauenswuerdig"
        });
    }

    // Testzweck: Widerspruechliche Modi, Arten und Kennungen duerfen nicht still auf eine
    // andere Identitaet zeigen oder zu Freitextberechtigungen herabgestuft werden.
    [Test]
    public void DirectoryAssignment_ShouldRejectMixedOrContradictoryValues()
    {
        var id = Guid.NewGuid();
        Action mixed = () => new FolderAssignmentDto
        {
            ReferenceMode = "text", SubjectKind = "user", Subject = "anna", Role = "editor",
            SubjectRef = new SubjectRefDto { Kind = "user", Id = id }
        }.ToModel();
        Action contradictory = () => new FolderAssignmentDto
        {
            ReferenceMode = "directory", SubjectKind = "group", Subject = Guid.NewGuid().ToString(), Role = "editor",
            SubjectRef = new SubjectRefDto { Kind = "user", Id = id }
        }.ToModel();

        mixed.Should().Throw<ArgumentException>();
        contradictory.Should().Throw<ArgumentException>();
    }
}
