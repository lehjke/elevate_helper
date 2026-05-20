using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

internal sealed class ElevateWorkflowRunner
{
    private readonly ElevateRunManifestService manifestService;

    public ElevateWorkflowRunner(ElevateRunManifestService manifestService)
    {
        this.manifestService = manifestService;
    }

    public async Task<ProcessingResult> RunAsync(
        ElevateWorkflowRunRequest request,
        IReadOnlyList<ElevateWorkflowStep> steps,
        CancellationToken cancellationToken)
    {
        ElevateRunManifest manifest = manifestService.Create(
            request.WorkingFolder,
            request.BuildingType,
            request.IncludeLunchPeak,
            request.CopiesCount);

        foreach (ElevateWorkflowStep workflowStep in steps)
        {
            ElevateRunManifestStep manifestStep = manifestService.StartStep(manifest, workflowStep.Name);
            ElevateWorkflowStepResult stepResult;

            try
            {
                stepResult = await workflowStep.ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                return StopRun(manifest, manifestStep, "Run was stopped early.", ex);
            }
            catch (Exception ex)
            {
                string message = workflowStep.FormatException(ex);
                return FailRun(manifest, manifestStep, message, ex);
            }

            if (stepResult.Artifacts is not null)
            {
                manifestService.SetArtifacts(manifest, stepResult.Artifacts);
            }

            if (!stepResult.Success)
            {
                return FailRun(manifest, manifestStep, stepResult.Message, stepResult.Exception);
            }

            manifestService.CompleteStep(manifest, manifestStep);
        }

        manifestService.Complete(manifest);
        return ProcessingResult.Ok("OK!");
    }

    private ProcessingResult FailRun(
        ElevateRunManifest manifest,
        ElevateRunManifestStep failedStep,
        string message,
        Exception? exception = null)
    {
        manifestService.FailStep(manifest, failedStep, message);
        manifestService.Fail(manifest, message);
        return ProcessingResult.Fail(message, exception);
    }

    private ProcessingResult StopRun(
        ElevateRunManifest manifest,
        ElevateRunManifestStep stoppedStep,
        string message,
        Exception? exception = null)
    {
        manifestService.StopStep(manifest, stoppedStep, message);
        manifestService.Stop(manifest, message);
        return ProcessingResult.Fail(message, exception);
    }
}

internal sealed record ElevateWorkflowRunRequest(
    string WorkingFolder,
    BuildingType BuildingType,
    bool IncludeLunchPeak,
    int CopiesCount);

internal sealed class ElevateWorkflowStep
{
    public ElevateWorkflowStep(
        string name,
        string exceptionOperationName,
        Func<CancellationToken, Task<ElevateWorkflowStepResult>> executeAsync)
    {
        Name = name;
        ExceptionOperationName = exceptionOperationName;
        ExecuteAsync = executeAsync;
    }

    public string Name { get; }

    public string ExceptionOperationName { get; }

    public Func<CancellationToken, Task<ElevateWorkflowStepResult>> ExecuteAsync { get; }

    public string FormatException(Exception exception)
    {
        return $"An exception of type {exception.GetType().Name} occurred in {ExceptionOperationName}. {exception.Message}";
    }
}

internal sealed record ElevateWorkflowStepResult(
    bool Success,
    string Message,
    Exception? Exception = null,
    IReadOnlyList<ElevateRunManifestArtifact>? Artifacts = null)
{
    public static ElevateWorkflowStepResult Completed(
        IReadOnlyList<ElevateRunManifestArtifact>? artifacts = null)
    {
        return new ElevateWorkflowStepResult(true, string.Empty, Artifacts: artifacts);
    }

    public static ElevateWorkflowStepResult Failed(string message, Exception? exception = null)
    {
        return new ElevateWorkflowStepResult(false, message, exception);
    }
}
