# UX Analyzer — Memory Index

## Project
- [Markdown Rendering UX Patterns](project_markdown_rendering_ux_patterns.md) — US-4.1: double Tick subscription on re-attach (HIGH), bubbles/auto-scroll carry-forward (HIGH x2), debounce gap at stream end (MEDIUM), non-clickable inline links (MEDIUM), code language label (MEDIUM), AutomationProperties missing (MEDIUM)
- [Settings View UX Patterns](project_settings_ux_patterns.md) — US-4.3: SaveSettings() must be private (HIGH), stale success banner on revert (MEDIUM), no AutomationProperties on NumericUpDown/API keys (MEDIUM x2), CloudModel free-text carry-forward (MEDIUM)
- [Memory Graph UX Patterns](project_memory_graph_ux_patterns.md) — US-2.6 review findings: color palette deviation, animation invalidation bug (OnPropertyChanged does not trigger InvalidateVisual), IsSimulating not bound, detail panel concatenated string, filter panel missing bulk select
- [CLI MCP Command Patterns](project_cli_mcp_patterns.md) — US-3.3 review: spinner output placement, disconnect green-in-catch, servers table missing args, help alignment
- [Onboarding Wizard UX Patterns](project_onboarding_wizard_ux_patterns.md) — US-3.4 review: API key masking (HIGH), ollama pull progress swallowed in spinner (HIGH), auto-trigger scope too broad, no step numbering
- [Chat Streaming UX Patterns](project_chat_streaming_ux_patterns.md) — US-3.5 review: tool-token leak into buffer (HIGH), no auto-scroll (HIGH), identical message bubbles (HIGH), IsProcessing race, no spinner, double IsEnabled binding

- [Empty State UX Patterns](project_empty_state_ux_patterns.md) — US-4.4 review: 4 carry-forward HIGH from US-3.5 (spinner, auto-scroll, bubbles, IsEnabled); flash-on-load MEDIUM; prompt auto-send MEDIUM
- [Chat UX Polish Patterns](project_chat_ux_polish_patterns.md) — AC-1/2/3/4: auto-scroll RESOLVED; bubble styles defined but HIGH blocker: inline Background="#313244" overrides user-bubble style setter (Avalonia local-value precedence); ErrorDetail opacity MEDIUM carry-forward; AutomationProperties MEDIUM carry-forward
- [Error Handling UX Patterns](project_error_handling_ux_patterns.md) — US-4.2 review: RetryAsync CanExecute bypass (HIGH), auto-scroll/bubbles carry-forward (HIGH x2), ErrorClassifier HTTP 401/403 misclassification (MEDIUM), detail not expandable (MEDIUM)
- [Action Log Real-Time UX Patterns](project_action_log_realtime_patterns.md) — US-4.5 AC-10: singleton DI required; scroll anchor MEDIUM; IsLoading not bound MEDIUM; duplicate race LOW; token N0 format LOW

## Feedback
- [Async ViewModel Processing Guard](feedback_async_viewmodel_processing_guard.md) — Every async RelayCommand awaiting AgentService must set IsProcessing=true before await and bind button IsEnabled to !IsProcessing
