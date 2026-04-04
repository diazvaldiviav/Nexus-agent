---
name: US-4.1 Markdown Rendering Validation
description: Validation result for US-4.1 MarkdownRenderer + MarkdownTextBlock + ChatMessage.IsAssistantNormal + ChatView AXAML update
type: project
---

Decision: APPROVED (0 HIGH, 2 MEDIUM, 3 LOW)

**Why:** Desktop-only change. No layer violations. Static class pattern consistent with ErrorClassifier. DispatcherTimer lifecycle needs IDisposable on MarkdownTextBlock. [NotifyPropertyChangedFor] placement needs correction.

**MEDIUM-1:** [NotifyPropertyChangedFor(nameof(IsAssistantNormal))] is specified on IsError, but IsAssistantNormal also depends on IsUser (computed as !IsUser && !IsError). IsUser is a computed property (not [ObservableProperty]), so it never raises PropertyChanged. If Role changes (unlikely but possible), IsAssistantNormal won't update. More critically, on initial add, the attribute placement on IsError is correct because IsUser is init-only and never changes. This is acceptable but must be documented.

**MEDIUM-2:** MarkdownTextBlock owns a DispatcherTimer but the architecture doc specifies no IDisposable/cleanup. The timer must be stopped and the Tick handler unregistered when the control is detached from the visual tree — otherwise the timer fires on orphaned controls. Fix: override OnDetachedFromVisualTree() to call _debounceTimer.Stop() and unregister the Tick handler.

**LOW-1:** Markdig 0.38.0 added to Nexus.Desktop.Tests.csproj. Since MarkdownRendererTests only call MarkdownRenderer.Render() which is in Nexus.Desktop, Markdig is a transitive reference via Nexus.Desktop. The explicit Markdig reference in the test project is redundant but harmless.

**LOW-2:** Architecture says "Monospace Run for inline code" — in Avalonia 11.x there is no Run equivalent in StackPanel/WrapPanel contexts. The design acknowledges "no per-Run background in Avalonia 11", which is correct. But using SelectableTextBlock vs TextBlock for code blocks is not specified — implementer should clarify.

**LOW-3:** Process.Start for links requires UseShellExecute=true on some platforms. Architecture specifies http/https scheme validation, but does not mention ProcessStartInfo.UseShellExecute=true — implementer must set this explicitly or links will fail silently on Linux.

Codebase confirmed:
- ChatMessage.IsError is [ObservableProperty] (line 18 of ChatViewModel.cs) — [NotifyPropertyChangedFor] placement on it is valid
- ChatMessage.IsUser is a simple computed property (not [ObservableProperty]) — Role is init-only, so IsUser never changes after construction
- GraphCanvas uses ImmutableSolidColorBrush/ImmutablePen (correct pattern) — MarkdownRenderer can use Avalonia Colors directly for inline styling
- ErrorClassifier is static class with no DI — MarkdownRenderer follows same pattern correctly
- Nexus.Desktop.csproj has no Markdig reference yet — must add it
- Nexus.Desktop.Tests.csproj already has Avalonia.Headless.XUnit 11.2.5

**How to apply:** When implementing US-4.1, add OnDetachedFromVisualTree() cleanup to MarkdownTextBlock. Add UseShellExecute=true to Process.Start. The [NotifyPropertyChangedFor(nameof(IsAssistantNormal))] on IsError is the correct and only needed placement.
