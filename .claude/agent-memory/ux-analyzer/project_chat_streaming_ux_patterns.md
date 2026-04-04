---
name: Chat Streaming UX Patterns
description: US-3.5 review findings — token leak, auto-scroll absence, message visual distinction, IsProcessing race, StatusText entity-count race
type: project
---

US-3.5 introduced streaming via ChatStreamAsync. Three HIGH issues found in the first review:

1. **[Executing tool:] token leaks into accumulated buffer** — `accumulated.Append(token)` runs before the `[Executing tool:]` guard check. Fix: move the guard above the Append call so the tool-status token is never buffered.

2. **No auto-scroll during streaming** — ScrollViewer has no scroll-to-end behavior. Messages grow vertically off-screen during token streaming. Fix: name the ScrollViewer and subscribe to CollectionChanged + the last ChatMessage's PropertyChanged to call ScrollToEnd.

3. **User vs assistant bubbles look identical** — same background (#313244), same left alignment. Only a 10px opacity-0.5 "Role" label distinguishes them. Fix: add HorizontalAlignment and Background converters per IsUser.

Medium issues:
- `IsProcessing = false` in `finally` not awaited on UI thread — last content posts may not have executed yet (race).
- No ProgressBar/spinner — only "Processing..." text at 60% opacity; indeterminate ProgressBar needed.
- No AutomationProperties.Name on Send button or message TextBox.

Low issues:
- Double IsEnabled binding on Send button (explicit + CanExecute) can desynchronize; use [NotifyCanExecuteChangedFor] on _inputText and _isProcessing instead.
- StatusText "Ready" reset has a timing race with background entity extraction callback.
- ModelInfo field never populated on success path; always empty in UI.

**Why:** Streaming adds async complexity: tokens arrive on non-UI threads, background tasks fire after yield returns, and multiple Dispatcher.Post calls must be ordered correctly.

**How to apply:** When reviewing any incremental-update ViewModel, verify: (1) filtered tokens are not appended before the filter check, (2) ScrollViewer has scroll-to-end wiring, (3) message role is visually distinct beyond a label, (4) IsProcessing reset is awaited on the UI thread in finally.
