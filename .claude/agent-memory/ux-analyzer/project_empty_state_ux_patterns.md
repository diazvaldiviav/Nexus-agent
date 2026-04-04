---
name: Empty State UX Patterns
description: US-4.4 review findings — empty state visibility logic, flash-on-load bug, example prompt affordance, carry-forward HIGH issues from US-3.5
type: project
---

## US-4.4 Empty State Feature — Structural Quality

The empty state feature is correctly implemented at the MVVM binding level:
- HasMessages / HasNodes / HasActions computed properties use CollectionChanged wiring in all three ViewModels.
- IsVisible="{Binding !HasMessages}" negation bindings are correct Avalonia syntax.
- SetExamplePromptCommand uses CommandParameter binding — correct CommunityToolkit.Mvvm pattern.
- Empty state copy is contextually appropriate per view.
- Three-button example prompt layout uses consistent Padding="12,8" and Spacing="8".

## Carry-Forward HIGH Issues (US-3.5, still unresolved as of US-4.4 review)

1. **Send button missing IsEnabled="{Binding !IsProcessing}"** — ChatView.axaml line 28. CanExecute alone is insufficient per the project feedback guard pattern.

2. **No ProgressBar spinner during streaming** — ChatView.axaml lacks `<ProgressBar DockPanel.Dock="Top" IsIndeterminate="True" Height="2" IsVisible="{Binding IsProcessing}" />` after the header border. Only StatusText at 60% opacity provides processing feedback.

3. **User vs assistant bubbles visually identical** — same Background="#313244", same left alignment. IsUser property exists on ChatMessage but is not used for visual differentiation.

4. **No auto-scroll to latest message** — ScrollViewer is unnamed, no ScrollToEnd() wiring on CollectionChanged or ChatMessage.Content PropertyChanged.

## Empty State Flash-on-Load (MEDIUM, new)

During LoadGraphAsync (MemoryGraphViewModel) and LoadActionsAsync (ActionLogViewModel), the observable collections are cleared before refill. This fires CollectionChanged → HasNodes/HasActions = false → empty state briefly flashes even when data exists.

Fix pattern: expose `ShowEmptyState => !HasNodes && !IsLoading` (and equivalent for ActionLog). Use [NotifyPropertyChangedFor(nameof(ShowEmptyState))] on _isLoading. Bind XAML to ShowEmptyState instead of !HasNodes.

## Detail Panel Still Concatenated String (MEDIUM, carry-forward)

MemoryGraphViewModel.SelectedNodeDetails is a raw multi-line string. XAML binds a single TextBlock. Should be replaced with structured Run/TextBlock elements bound directly to SelectedNode.Name, SelectedNode.Type, SelectedNode.RelevanceScore, SelectedNode.Summary.

## Example Prompt Buttons Should Auto-Send (MEDIUM)

SetExamplePromptCommand only populates InputText. Users must manually press Send. Example prompt buttons carry an implicit "start conversation" affordance. Fix: rename to SetExamplePromptAndSendAsync, chain InputText = prompt then await SendAsync() if CanSend().

## Timestamp and Token Formatting (PARTIAL — ActionLogView)

ActionLogView.axaml: Timestamp column NOW has StringFormat='{}{0:yyyy-MM-dd HH:mm:ss}' (resolved as of US-4.5 AC-10 review).
TokensIn/TokensOut still have NO StringFormat. Apply '{}{0:N0}' to both token columns.

## ModelInfo Never Populated on Success Path (LOW)

ChatViewModel: assistantMessage.ModelInfo is set to "error" in catch but never populated on the streaming success path. The ModelInfo TextBlock is always blank for successful responses. Remove the row until streaming surfaces model name metadata.

**Why:** The empty state feature exposes the populated state more prominently — the first interaction after empty state dismissal is the most critical UX moment, making the chat view HIGH issues more impactful than before.

**How to apply:** When reviewing ChatView in any future sprint, treat all four carry-forward HIGH issues (spinner, scroll, bubbles, IsEnabled) as must-fix before COMPLIANT. Do not approve ChatView changes that leave these open.

**Status update (US-4.1 review, 2026-04-04):** Auto-scroll and bubble differentiation remain unresolved for the fourth consecutive sprint. ProgressBar spinner and IsEnabled guard were resolved in Sprint 4 Day 1. Scroll and bubbles are now HIGH blockers — they must not be deferred into any future sprint.

**Status update (Chat UX Polish review, 2026-04-04):** Auto-scroll RESOLVED — full ChatView.axaml.cs implementation (CollectionChanged + Content PropertyChanged, _autoScrollEnabled guard, _isProgrammaticScroll guard, OnLoaded/OnUnloaded lifecycle). Bubble differentiation PARTIALLY resolved — user-bubble style defined with correct color/alignment/MaxWidth, but blocked by inline Background="313244" local-value override bug (HIGH). See project_chat_ux_polish_patterns.md.
