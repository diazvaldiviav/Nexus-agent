using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nexus.Core.Config;

namespace Nexus.Core.Services;

/// <summary>
/// Deterministic, no-LLM heuristic that decides whether the planner should be invoked
/// for a given user message. Short messages and conversational input (greetings,
/// acknowledgements, simple questions) are blocked before the planner LLM call.
/// </summary>
internal static class PlannerInvocationHeuristic
{
    // ── Pre-compiled regexes ──────────────────────────────────────────────────

    private static readonly Regex ImperativeVerbRegex = new(
        @"\b(?:crea|crear|creame|haz|hazme|escribe|escribir|a[nñ]ade|anade|agrega|agregar|" +
        @"borra|borrar|elimina|eliminar|mueve|mover|renombra|renombrar|copia|copiar|lee|" +
        @"leer|abre|abrir|busca|buscar|encuentra|encontrar|lista|listar|muestra|mostrar|" +
        @"dame|ense[nñ]ame|ensename|edita|editar|modifica|modificar|" +
        @"create|make|write|add|delete|remove|move|rename|copy|read|open|search|find|" +
        @"list|show|give|edit|modify)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PathPatternRegex = new(
        @"(?:[A-Za-z]:[\\/]|[\\/])[\w.\-]+",
        RegexOptions.Compiled);

    private static readonly Regex FileExtensionRegex = new(
        @"\.\w{1,8}\b",
        RegexOptions.Compiled);

    // ── Curated chat / greeting set (de-accented, lowercase) ─────────────────

    private static readonly HashSet<string> GreetingSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "hola", "hi", "hello", "hey",
        "gracias", "thanks", "thank you",
        "ok", "okay", "vale",
        "si", "no",
        "adios", "bye",
        "como estas", "how are you",
        "que tal",
        "buenos dias", "buenas",
        "saludos", "chao",
        "?", "??", "…"   // … (horizontal ellipsis)
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>(ShouldPlan, Reason)</c>. Never throws.
    /// </summary>
    /// <param name="userMessage">The raw user message.</param>
    /// <param name="config">Current Nexus configuration (reads <c>Mcp.PlannerHeuristicMinLength</c>).</param>
    public static (bool ShouldPlan, string Reason) ShouldInvokePlanner(string userMessage, NexusConfig config)
    {
        try
        {
            return Evaluate(userMessage, config);
        }
        catch
        {
            return (true, "fallback_default_allow");
        }
    }

    // ── Private implementation ────────────────────────────────────────────────

    private static (bool ShouldPlan, string Reason) Evaluate(string userMessage, NexusConfig config)
    {
        // Step 1: length check
        var trimmed = userMessage?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return (false, "below_min_length");

        var minLength = config.Mcp.PlannerHeuristicMinLength;
        if (trimmed.Length < minLength)
            return (false, "below_min_length");

        // Step 2: greeting check — de-accent, lowercase, strip trailing punctuation
        var deAccented = DeAccent(trimmed.ToLowerInvariant());
        var stripped = StripTrailingPunctuation(deAccented);
        if (GreetingSet.Contains(stripped))
            return (false, "chat_greeting");

        // Step 3: strong-positive triggers
        if (ImperativeVerbRegex.IsMatch(deAccented))
            return (true, "imperative_verb");

        if (PathPatternRegex.IsMatch(trimmed))
            return (true, "path_match");

        if (FileExtensionRegex.IsMatch(trimmed))
            return (true, "file_extension");

        // Step 4: default allow
        return (true, "default_allow");
    }

    /// <summary>
    /// Strips Unicode combining marks (de-accents) from the string.
    /// Uses <see cref="NormalizationForm.FormD"/> decomposition then filters combining chars.
    /// </summary>
    private static string DeAccent(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Removes trailing <c>?</c>, <c>!</c>, <c>.</c>, <c>…</c> characters (one or more).
    /// </summary>
    private static string StripTrailingPunctuation(string input)
    {
        var result = input.TrimEnd('?', '!', '.', '…');
        // If stripping leaves nothing, return the original so we can still match "?" etc.
        return result.Length == 0 ? input : result;
    }
}
