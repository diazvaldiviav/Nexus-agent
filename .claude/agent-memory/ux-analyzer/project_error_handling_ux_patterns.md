---
name: Error Handling & Recovery UX Patterns
description: US-4.2 review findings — error banner structure, ErrorClassifier HTTP 401/403 bug, RetryAsync CanExecute bypass, error detail expand/collapse, banner duplication
type: project
---

## US-4.2 Error Handling & Recovery — Structural Quality

ChatView error banner is structurally correct: DockPanel with Retry/Dismiss buttons on the right, message+detail stacked on the left. `HasError` binding controls visibility. `ErrorMessage` is `#f38ba8` (Catppuccin error red). `ErrorDetail` is `#a6adc8` (subtext1). The `Classes.error` style applies `#4df38ba8` tint to failed message bubbles. SettingsView success/error banners below Save button use `HasSuccess`/`HasError` with `#4da6e3a1`/`#4df38ba8` backgrounds — correct colors.

## HIGH Issues

1. **RetryAsync bypasses CanExecute guard** — `RetryAsync()` calls `await SendAsync()` directly, skipping `CanSend()`. A double-retry under concurrent conditions can enter `SendAsync` while `IsProcessing` is true. Fix: add `if (CanSend())` guard before `await SendAsync()`, or call `SendCommand.ExecuteAsync(null)`.

2. **Auto-scroll absent** (carry-forward from US-3.5/US-4.4) — error messages land off-screen. Name ScrollViewer `x:Name="MessagesScrollViewer"` and wire `ScrollToEnd()` on CollectionChanged.

3. **User vs assistant bubbles identical** (carry-forward from US-3.5/US-4.4) — `IsUser` property exists but is not used for visual differentiation. Add BoolToBubbleBrush and BoolToAlign converters.

## MEDIUM Issues

4. **ErrorClassifier HTTP 401/403 misclassification** — `HttpRequestException` is caught before the "unauthorized" string check. Fix: check `httpEx.StatusCode` in the `HttpRequestException` branch first; return API key error message for 401/403.

5. **ErrorDetail not expandable (AC-4 not fully met)** — `ErrorDetail` TextBlock is always visible. AC-4 specifies expandable detail. Add `ShowErrorDetail` bool + `ToggleErrorDetailCommand` to ViewModel, or use `<Expander>`.

6. **ErrorDetail double opacity reduction** — `Foreground="#a6adc8"` combined with `Opacity="0.7"` drops below WCAG AA contrast against the error tint background. Remove `Opacity="0.7"`.

7. **Error message duplicated in banner and bubble** — same `userMsg` text appears in both the banner and `assistantMessage.Content`. Set bubble content to empty string or placeholder; rely on `IsError=true` tint alone.

8. **Settings success banner never auto-dismisses** — `HasSuccess = true` is never reset. Add `Task.Delay(3000)` + reset in SettingsViewModel.SaveSettings().

9. **Error color hex duplicated** — `#4df38ba8` appears in both the `Border.error` style and the banner `Background` inline. Extract to `App.axaml` resource.

## LOW Issues

10. **ClearHistoryAsync finally block does not use DispatchToUI** — inconsistent threading pattern. Wrap `IsProcessing = false` in `DispatchToUI`.

11. **No AutomationProperties.HelpText on Retry/Dismiss buttons** — not blocking but should be added.

## Carry-Forward Status (as of US-4.2 review)

- Auto-scroll: 3rd sprint in a row unresolved. Must not be deferred again.
- Message bubble differentiation: 3rd sprint in a row unresolved. Must not be deferred again.
- CloudModel ComboBox: still a free-text TextBox; pre-dates US-4.2.
- API key TextBox Watermark: still absent; pre-dates US-4.2.

**Why:** Error states are the highest-stakes UX moments — users need clear, non-duplicated, well-contrasted feedback with a working retry path. The CanExecute bypass in RetryAsync is a correctness issue, not just cosmetic.

**How to apply:** When reviewing any ViewModel that has both a primary RelayCommand and a secondary "retry" command, verify the retry does not call the primary method directly — it must route through CanExecute. Also verify ErrorClassifier covers HTTP status codes, not just message strings.
