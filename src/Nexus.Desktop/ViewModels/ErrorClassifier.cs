namespace Nexus.Desktop.ViewModels;

public static class ErrorClassifier
{
    public static (string Category, string UserMessage, string Detail) Classify(Exception ex)
    {
        var detail = ex.Message;

        if (ex is HttpRequestException)
            return ("connection", "Could not connect to the AI model. Check that Ollama is running or verify your network connection.", detail);

        if (ex is TaskCanceledException or TimeoutException)
            return ("timeout", "The request timed out. The model may be overloaded. Try again.", detail);

        if (ex.Message.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return ("apikey", "Authentication failed. Check your API key in Settings.", detail);

        return ("generic", "An unexpected error occurred.", detail);
    }
}
