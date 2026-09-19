using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.ProcessPackages;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Prozesspakete: einen Workflow samt Formularen aus einer Installation heraus- und in eine
/// andere hineintragen.
///
/// Bewusst ein eigener Controller neben <see cref="DefinitionController"/>, aber unter
/// derselben Route: Fuer die Bedienung gehoert das Paket zum Workflow, fuer den Code ist es ein
/// abgeschlossenes Thema mit eigenem Format, eigenen Grenzen und eigenen Pruefungen.
/// </summary>
[ApiController]
[Route("definition")]
public sealed class ProcessPackageController(
    ProcessPackageService packageService,
    FolderBusinessLogic folderBusinessLogic) : FlowzerControllerBase
{
    /// <summary>
    /// Liefert den Workflow als Paket. Die Berechtigung ist dieselbe wie fuers Lesen: Wer den
    /// Workflow im Katalog sieht, darf ihn auch mitnehmen. Ein Paket enthaelt keine Secrets,
    /// keine Instanzen und keine Personenkennungen — es gibt also nichts preis, was nicht schon
    /// im Modellierer sichtbar waere.
    /// </summary>
    // Bewusst ohne [Produces("application/zip")]: Das schriebe den Antworttyp fuer die ganze
    // Aktion fest, und jede Fehlermeldung — die JSON ist — endete in einer 406 statt in ihrem
    // eigenen Statuscode. Den Typ setzt das FileResult des Erfolgsfalls selbst.
    [HttpGet("meta/{id}/package")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/zip")]
    [ProducesResponseType<ApiStatusResult<ProcessPackagePreviewDto>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiStatusResult<ProcessPackagePreviewDto>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> ExportPackage([FromRoute] string id)
    {
        ProcessPackageService.Package package;
        try
        {
            package = await packageService.ExportAsync(id);
        }
        catch (ProcessPackageException failure)
        {
            return PackageProblem(failure);
        }

        return File(package.Content, "application/zip", package.FileName);
    }

    /// <summary>
    /// Liest ein hochgeladenes Paket, ohne etwas anzulegen: Was steht drin, wuerde die
    /// Veroeffentlichung das Modell hier annehmen, welche Bezuege sind zuzuordnen und steht eine
    /// vorhandene Kennung im Weg.
    /// </summary>
    [HttpPost("package/preview")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    [ProducesResponseType<ApiStatusResult<ProcessPackagePreviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<ProcessPackagePreviewDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<ProcessPackagePreviewDto>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiStatusResult<ProcessPackagePreviewDto>>> PreviewPackage(IFormFile? package)
    {
        if (ReadUpload(package) is not { } upload)
            return BadRequest(new ApiStatusResult<ProcessPackagePreviewDto>(MissingUpload));

        await using var stream = upload;
        try
        {
            var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
            return Ok(new ApiStatusResult<ProcessPackagePreviewDto>(
                await packageService.PreviewAsync(stream, permissions)));
        }
        catch (ProcessPackageException failure)
        {
            return PackageProblem<ProcessPackagePreviewDto>(failure);
        }
    }

    /// <summary>
    /// Legt den Workflow und seine Formulare an. <c>mapping</c> traegt die Zielentscheidung und
    /// die Zuordnungen; ohne sie wird nichts geraten.
    ///
    /// Veroeffentlicht wird ausdruecklich nicht. Ein importiertes Modell laeuft erst, wenn
    /// jemand es im Modellierer bewusst in Betrieb nimmt.
    /// </summary>
    [HttpPost("package/import")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    [ProducesResponseType<ApiStatusResult<ProcessPackageImportResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<ProcessPackageImportResultDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<ProcessPackageImportResultDto>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiStatusResult<ProcessPackageImportResultDto>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiStatusResult<ProcessPackageImportResultDto>>> ImportPackage(
        IFormFile? package,
        [FromForm] string? mapping)
    {
        if (ReadUpload(package) is not { } upload)
            return BadRequest(new ApiStatusResult<ProcessPackageImportResultDto>(MissingUpload));

        await using var stream = upload;

        ProcessPackageMappingDto? parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(mapping)
                ? null
                : JsonSerializer.Deserialize<ProcessPackageMappingDto>(mapping, ProcessPackageFormat.JsonOptions);
        }
        catch (JsonException)
        {
            return BadRequest(new ApiStatusResult<ProcessPackageImportResultDto>(
                "Das Feld „mapping“ ist kein lesbares JSON."));
        }

        if (parsed is null)
            return BadRequest(new ApiStatusResult<ProcessPackageImportResultDto>(
                "Ohne das Feld „mapping“ ist nicht entschieden, wohin der Import gehen soll."));

        try
        {
            var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
            return Ok(new ApiStatusResult<ProcessPackageImportResultDto>(
                await packageService.ImportAsync(stream, parsed, permissions)));
        }
        catch (ProcessPackageException failure)
        {
            return PackageProblem<ProcessPackageImportResultDto>(failure);
        }
    }

    private const string MissingUpload = "Es wurde keine Paketdatei im Feld „package“ hochgeladen.";

    /// <summary>
    /// Der Upload als Strom. Die Groesse wird schon hier geprueft, damit eine zu grosse Datei
    /// nicht erst beim Entpacken auffaellt.
    /// </summary>
    private Stream? ReadUpload(IFormFile? package)
    {
        if (package is null || package.Length == 0) return null;
        if (package.Length > ProcessPackageFormat.MaxUncompressedBytes)
            throw new BadHttpRequestException(
                $"Die Paketdatei überschreitet {ProcessPackageFormat.MaxUncompressedBytes} Bytes.",
                StatusCodes.Status413PayloadTooLarge);

        return package.OpenReadStream();
    }

    /// <summary>
    /// Ordnet eine Ablehnung ein. <c>package.import.forbidden</c> ist eine fehlende
    /// Zustaendigkeit und bekommt denselben Antwortheader wie jede andere; ein unlesbares Paket
    /// ist 400, ein lesbares mit unbrauchbarem Inhalt 422.
    /// </summary>
    private ActionResult<ApiStatusResult<T>> PackageProblem<T>(ProcessPackageException failure)
    {
        if (failure.Code == "package.import.forbidden") return ForbiddenCapability<T>(failure.Message);
        if (failure.Code.EndsWith(".not_found", StringComparison.Ordinal))
            return NotFound(new ApiStatusResult<T>(failure.Message));

        var result = new ApiStatusResult<T>(failure.Message);
        return failure.Unprocessable
            ? StatusCode(StatusCodes.Status422UnprocessableEntity, result)
            : BadRequest(result);
    }

    private ActionResult PackageProblem(ProcessPackageException failure) =>
        PackageProblem<ProcessPackagePreviewDto>(failure).Result!;
}
