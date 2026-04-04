---
name: Markdown Rendering UX Patterns
description: US-4.1 review findings — double Tick subscription, non-clickable inline links, debounce gap at stream end, code language label, auto-scroll and bubble carry-forwards
type: project
---

## US-4.1 Markdown Rendering — Structural Quality

MarkdownRenderer is a correct static helper: Catppuccin Mocha colors (#cdd6f4 text, #89b4fa link, #1e1e2e code bg, #313244 code border), ImmutableBrush/Pen caching, never-throws contract with plain-TextBlock fallback. MarkdownTextBlock uses StyledProperty + DispatcherTimer debounce (250ms) with _isDetached guard and OnDetachedFromVisualTree cleanup.

IsAssistantNormal on ChatMessage drives TextBlock vs MarkdownTextBlock toggle — correct MVVM binding. Plain user messages remain as TextBlock (no-regression for AC-7).

## HIGH Issues

1. **Double Tick event subscription on re-attach** — Constructor wires `_debounceTimer.Tick += OnDebounceTimerTick`. OnAttachedToVisualTree also wires `_debounceTimer.Tick += OnDebounceTimerTick` unconditionally. Each ItemsControl virtualization cycle adds another handler. Fix: remove the Tick subscription from OnAttachedToVisualTree entirely.

2. **User vs assistant bubbles visually identical** (carry-forward from US-3.5/4.4/4.2) — Fourth sprint deferred. Both bubbles use Background="#313244" + left alignment. IsUser/IsAssistantNormal exist but are only used for the markdown/plain toggle. Fix: BoolToBubbleBrushConverter + BoolToAlignConverter on message Border, MaxWidth="480".

3. **No auto-scroll during streaming or on new message** (carry-forward from US-3.5/4.4/4.2) — Fourth sprint deferred. ScrollViewer unnamed. Fix: x:Name="MessagesScrollViewer", wire ScrollToEnd() in code-behind on CollectionChanged and last ChatMessage.Content PropertyChanged.

## MEDIUM Issues

4. **Debounce gap at stream end** — After last token, IsProcessing transitions to false before the 250ms timer fires. "Ready" status appears while markdown is still partially rendered. Fix: expose ForceRender() on MarkdownTextBlock and call it when IsProcessing becomes false (or add RenderComplete property to ChatMessage that the control observes).

5. **Non-clickable inline links show as clickable** — RenderLinkInline emits underlined blue Run with no PointerPressed. Avalonia 11 InlineUIContainer not supported. Users expect clickability. Fix: add ToolTip.Tip on the parent TextBlock with the URL list so users can copy it.

6. **Fenced code block language label missing** — FencedCodeBlock.Info contains the language hint but is unused. Fix: cast to FencedCodeBlock?, extract Info, display in top-right of code block Border using a DockPanel header TextBlock.

7. **No AutomationProperties on message Border or MarkdownTextBlock** — Screen reader has no accessible name for message containers. Fix: AutomationProperties.Name="{Binding Role}" on Border; MarkdownTextBlock static ctor syncs TextProperty → AutomationProperties.Name.

## LOW Issues

8. **IsAssistantNormal immutability assumption undocumented** — Role is init-only so IsUser never changes; NotifyPropertyChangedFor only covers IsError. Safe but fragile for future maintainers. Fix: add XML comment.

9. **ErrorDetail double opacity** (carry-forward from US-4.2) — Foreground="#a6adc8" + Opacity="0.7" falls below WCAG AA. Fix: remove Opacity="0.7".

**Why:** MarkdownTextBlock is reused in a virtualized ItemsControl. Double event subscriptions in re-attach lifecycle methods are a classic Avalonia trap — they compound silently until scroll performance degrades or renders fire out of order.

**How to apply:** When reviewing any control that subscribes to a DispatcherTimer.Tick, verify the subscription appears in exactly one place (constructor or OnAttachedToVisualTree, not both). OnDetachedFromVisualTree unsubscribe is required; constructor subscribe is preferred over OnAttachedToVisualTree subscribe to avoid re-attach multiplication.
