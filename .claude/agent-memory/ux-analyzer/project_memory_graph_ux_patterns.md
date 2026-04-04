---
name: Memory Graph UX Patterns
description: Known patterns, gaps, and bugs in MemoryGraphView/GraphCanvas/MemoryGraphViewModel; color palette deviations, animation fix status, filter logic inversion, and open defects
type: project
---

## Color Palette Deviation (HIGH — not yet fixed as of 2026-03-19)

GraphCanvas.GetNodeColor uses Catppuccin palette variants, NOT the canonical spec colors.
Correct mapping:
- Person: #4A90D9, Project: #27AE60, Technology: #F39C12
- Decision: #E74C3C, Date: #95A5A6, Preference: #8E44AD, Other: #FFFFFF

**Why:** The spec in avalonia-ux-principles/SKILL.md defines these hex values authoritatively. Any deviation breaks brand consistency and the UX review checklist.
**How to apply:** When reviewing or generating GraphCanvas code, always validate GetNodeBrush against this exact palette. Also note that white "Other" nodes require a label contrast fix (see below).

## Animation Invalidation Bug — RESOLVED (WI-HRD-1, 2026-03-19)

Prior bug: MemoryGraphViewModel used OnPropertyChanged(nameof(Nodes)) which did not trigger InvalidateVisual.

Current state: ViewModel exposes `public event EventHandler? LayoutUpdated` (line 56). LayoutTimerTick raises it (line 269). MemoryGraphView.axaml.cs subscribes in OnDataContextChanged, unsubscribes on VM swap (leak-safe), and calls `GraphCanvasControl.InvalidateVisual()`. The `x:Name="GraphCanvasControl"` in XAML matches the code-behind reference. Fully compliant.

**How to apply:** This pattern (event on VM, InvalidateVisual in code-behind) is the established approach for frame-driven animation in this project. Do not revert to OnPropertyChanged for animation.

## Select All / Clear All — Mechanically Implemented (WI-HRD-2, 2026-03-19), UX Defects Open

Commands SelectAllFiltersCommand / ClearAllFiltersCommand exist, are bound in XAML lines 22-23, and call ApplyFilterAndLayout() correctly.

Two UX defects introduced:
1. **Button label "None" is misleading** — should be "Clear" or "Deselect All".
2. **Logic inversion** — when selectedTypes.Count == 0, ApplyFilterAndLayout shows ALL nodes (MemoryGraphViewModel.cs line 186). Clicking "None/Clear All" makes everything visible, not nothing. Should show empty graph instead.

**Why:** Principle of least surprise — "None" selected should produce an empty canvas, not a full one.
**How to apply:** When reviewing filter logic, check the selectedTypes.Count == 0 guard. The intent of "Clear All" should be no nodes visible, not all nodes visible.

## IsSimulating Not Bound in XAML (MEDIUM — not yet fixed as of 2026-03-19)

MemoryGraphViewModel.IsSimulating [ObservableProperty] exists but is never bound in MemoryGraphView.axaml. No "Laying out..." indicator shown during force-directed convergence.

## Detail Panel Uses Concatenated String (MEDIUM — not yet fixed as of 2026-03-19)

SelectedNodeDetails is a raw multi-line string. The detail panel spec requires structured display of: name, type, summary, score, mentions, last seen. GraphNode does not expose Mentions or LastSeen fields yet. Detail panel XAML is a single TextBlock (MemoryGraphView.axaml lines 43-47).

## Refresh Button Not Disabled During Loading (MEDIUM — not yet fixed as of 2026-03-19)

Button "Refresh" (MemoryGraphView.axaml line 13) has no IsEnabled="{Binding !IsLoading}" binding. Violates the async processing guard pattern in feedback memory.

## Node Label Contrast Against Light Nodes (MEDIUM — not yet fixed as of 2026-03-19)

All node labels use Brushes.White (GraphCanvas.cs line 159). With spec-correct "Other" brush #FFFFFF, white label on white node = 1:1 contrast (unreadable). Need luminance-based label color selection.
