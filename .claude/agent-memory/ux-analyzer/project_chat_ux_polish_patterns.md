---
name: Chat UX Polish Patterns
description: Chat UX polish review findings — bubble differentiation (AC-1/2), MaxWidth (AC-2), auto-scroll (AC-3/4). Inline Background local-value override bug is the sole HIGH blocker.
type: project
---

## Review: Chat UX Polish (AC-1 through AC-4, Sprint 4 Day 6)

### What Was Delivered

AC-1: `Border.user-bubble` style defined with `Background="#45475a"` + `HorizontalAlignment="Right"`. Style applied via `Classes.user-bubble="{Binding IsUser}"` on the message Border.
AC-2: `MaxWidth="600"` on the `user-bubble` style — correct ~70% asymmetry.
AC-3/AC-4: Full auto-scroll implementation in ChatView.axaml.cs — CollectionChanged + ChatMessage.Content PropertyChanged both call ScrollToBottom(). `_autoScrollEnabled` flag suppressed when user scrolls up beyond 50px threshold. `_isProgrammaticScroll` guard prevents self-triggering. Lifecycle: OnLoaded subscribes, OnUnloaded unsubscribes, UntrackLastMessage prevents leaks.

### HIGH Blocker (must fix before COMPLIANT)

**Inline Background overrides user-bubble style setter** — `ChatView.axaml:99`: `Border` has inline `Background="#313244"`. In Avalonia 11, a locally-set property value (inline attribute) takes precedence over a Style setter, regardless of selector specificity. The `user-bubble` style's `Background="#45475a"` will be silently ignored at runtime. Both user and assistant bubbles render as `#313244`.

Fix:
1. Remove inline `Background="#313244"` from the Border element.
2. Add a `Border.message-bubble` style for the default background.
3. Apply `Classes="message-bubble"` alongside the conditional classes.
Avalonia style precedence: last matching style in document order wins — `user-bubble` defined after `message-bubble` will correctly override it because both are style setters, not local values.

### MEDIUM Issues (carry-forward)

- **ErrorDetail contrast below WCAG AA** — `Foreground="#a6adc8" Opacity="0.7"` on the error banner detail TextBlock (line 45). Fix: remove Opacity="0.7".
- **No AutomationProperties on TextBox, Send button, or message Border** — screen reader cannot announce role or action. Fix: add AutomationProperties.Name to all three.

### LOW Issues

- No "Jump to latest" affordance when auto-scroll is disabled. Optional UX improvement.
- Role label always visible, including on error bubbles. Fix: `IsVisible="{Binding !IsError}"` on the role TextBlock.

### Status

Auto-scroll: RESOLVED in this sprint (ChatView.axaml.cs full implementation).
Bubble differentiation: PARTIALLY resolved — styles are defined correctly but blocked by the inline Background override bug.

**Why:** Avalonia property value precedence is: Animation > Local > Trigger > Style > Default. Inline XAML attribute = Local. This trap is not caught by the compiler or at design time — it only manifests at runtime when the conditional class applies. Any future reviewer should check that style-dependent conditional classes are not competing with local values on the same property.

**How to apply:** When reviewing Avalonia views that use conditional `Classes.*` bindings for background/color, verify the property is NOT also set inline on the same element. If it is, move the default value to a base style class.
