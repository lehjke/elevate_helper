namespace ElevateHelperWinUI.Models;

public sealed record ProcessingResult(bool Success, string Message, Exception? Exception = null)
{
    public static ProcessingResult Ok(string message = "OK!")
    {
        return new ProcessingResult(true, message);
    }

    public static ProcessingResult Fail(string message, Exception? exception = null)
    {
        return new ProcessingResult(false, message, exception);
    }
}
