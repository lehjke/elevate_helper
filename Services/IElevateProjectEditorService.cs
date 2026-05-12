using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateProjectEditorService
{
    Task<ElevateProjectEditorDocument> LoadTemplate(
        BuildingType buildingType,
        CancellationToken cancellationToken = default);

    Task<ElevateProjectEditorDocument> LoadFile(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> SaveAsync(
        ElevateProjectEditorDocument document,
        string outputPath,
        CancellationToken cancellationToken = default);

    string SuggestFileName(ElevateProjectEditorDocument document);
}
