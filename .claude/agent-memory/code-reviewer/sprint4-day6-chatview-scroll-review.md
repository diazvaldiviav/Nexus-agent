---
name: Sprint 4 Day 6 — ChatView bubble + auto-scroll review
description: Review findings for ChatView.axaml bubble styles (AC-1/2) and ChatView.axaml.cs auto-scroll logic (AC-3/4). Key: programmatic-scroll race and DataContext-change leak.
type: project
---

Decision: APPROVED WITH SUGGESTIONS (0 HIGH, 3 MEDIUM, 3 LOW). AC-1 PASS. AC-2 PARTIAL FAIL. AC-3 AT RISK. AC-4 PASS.

**MEDIUM-1 (memory leak): ChatView.axaml.cs — DataContext cast in OnLoaded not guarded against ViewModel replacement.**
vm.Messages.CollectionChanged is subscribed on load; if DataContext changes later, OnUnloaded unsubscribes from the *new* VM (not the old one), leaving the old subscription alive.
Fix: subscribe DataContextChanged in constructor; unsubscribe old VM before subscribing new one.

**MEDIUM-2 (functional correctness, AC-3 at risk): OnScrollChanged fires during programmatic ScrollToEnd(), disabling _autoScrollEnabled immediately after a new message is added.**
Sequence: new message → ScrollToBottom posts to Background priority → layout runs → ScrollChanged fires with old offset → _autoScrollEnabled = false → Background post runs ScrollToEnd() but flag is already false → subsequent messages never auto-scroll.
Fix: add `_isProgrammaticScroll` flag; skip _autoScrollEnabled recalculation in OnScrollChanged when flag is true; clear flag inside the Background post lambda after ScrollToEnd().

**MEDIUM-3 (AC-2 partial fail): MaxWidth="600" is hardcoded pixels, not 70-80% of window width.**
Fails on panels wider than ~800px (50% width, not 75%). Correct fix: binding to $parent[ScrollViewer].Bounds.Width with a PercentConverter at 0.75. Pragmatic interim: raise to 900 with comment.

**LOW-1:** Background="#313244" inline on message Border — use named App.axaml resource when palette standardisation pass happens.
**LOW-2:** _trackedMessage PropertyChanged subscription added even when _autoScrollEnabled=false — wasteful but not buggy; gate subscription on _autoScrollEnabled.
**LOW-3:** DispatcherPriority.Background undocumented — add comment "Background priority ensures layout is complete before scroll" to prevent priority downgrade.

**Established pattern: _isProgrammaticScroll flag is the correct Avalonia idiom for separating user-initiated from code-initiated ScrollChanged events.**

Good patterns: OnLoaded/OnUnloaded symmetry; UntrackLastMessage() called in both OnMessagesCollectionChanged and OnUnloaded (no leak on mid-stream view destroy); PropertyChanged on last ChatMessage for streaming (zero-overhead when not streaming); DispatcherPriority.Background correct; Classes.user-bubble conditional style is clean MVVM (no code-behind imperative logic); _autoScrollEnabled=true initial value correct.
