---
name: Action Log Real-Time UX Patterns
description: US-4.5 AC-10 review findings — singleton DI, event subscription/disposal, scroll anchor, IsLoading binding, duplicate race guard, token formatting
type: project
---

## US-4.5 AC-10 — Real-Time Action Log (COMPLIANT, 0 HIGH / 2 MEDIUM / 3 LOW)

### Correct Patterns Established

- `AddSingleton<ActionLogViewModel>` is required for event-subscribed ViewModels. Transient would re-subscribe on every navigation, leaking handlers.
- `IDisposable` with `_notifier.ActionLogged -= OnActionLogged` in `Dispose()` is the correct cleanup pattern. `GC.SuppressFinalize` must be present.
- `_disposed` guard at top of event handler prevents post-dispose invocations.
- `Dispatcher.UIThread.Post(action)` is the correct fire-and-forget cross-thread UI update mechanism in Avalonia (not InvokeAsync, not BeginInvokeOnMainThread).
- `Actions.Insert(0, action)` for newest-at-top is correct for action log semantics.
- `OnFilterTypeChanged` → `LoadActionsAsync` (Clear + reload from DB) correctly reconciles in-memory real-time state when filter changes.
- `KnowledgeGraph` fires `ActionLogged` only AFTER the DB write completes, so events are always for persisted data.
- `IActionLogNotifier` registered as `AddSingleton<IActionLogNotifier>(knowledgeGraph)` — same instance as `IKnowledgeGraph`. No instance mismatch.

### MEDIUM Issues (open as of US-4.5 AC-10)

1. **Scroll anchor after Insert(0, action) not wired.** DataGrid has no scroll-to-row-0 after real-time insert. User scrolled down will not see new rows. Fix: code-behind subscribes to Actions.CollectionChanged and calls dataGrid.ScrollIntoView on insert at index 0.

2. **IsLoading not bound in ActionLogView.axaml.** ViewModel sets IsLoading correctly but XAML has no ProgressBar or Refresh button IsEnabled guard. Pattern: `<ProgressBar DockPanel.Dock="Top" IsIndeterminate="True" Height="2" IsVisible="{Binding IsLoading}" />` and `<Button IsEnabled="{Binding !IsLoading}" .../>`.

### LOW Issues (open as of US-4.5 AC-10)

3. **Duplicate race in real-time handler.** `Dispatcher.UIThread.Post` for an in-flight real-time action may execute after `LoadActionsAsync` calls `Actions.Clear()`. Result: one duplicate visible row. Fix: add `if (IsLoading) return;` at top of `OnActionLogged`.

4. **Token columns missing N0 StringFormat.** TokensIn and TokensOut are raw integers. Apply `StringFormat='{}{0:N0}'` to both DataGridTextColumn bindings.

5. **No AutomationProperties on ComboBox and Refresh Button.** Add `AutomationProperties.Name="Filter action type"` and `AutomationProperties.Name="Refresh action log"`.

**Why:** The duplicate race (LOW #3) is the highest-priority low — it is a 2-line guard in OnActionLogged with no architectural cost. Recommend fixing before sprint close.

**How to apply:** When reviewing ActionLogView in future sprints, treat scroll anchor (MEDIUM #1) and IsLoading binding (MEDIUM #2) as carry-forward open items until explicitly closed.
