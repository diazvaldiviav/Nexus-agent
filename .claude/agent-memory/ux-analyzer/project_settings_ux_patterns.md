---
name: Settings View UX Patterns
description: Established UX patterns and known gaps in SettingsView — API key fields, grouping, save feedback, validation, dirty tracking
type: project
---

The Settings view uses a DockPanel/ScrollViewer/StackPanel structure with grouped sections (Local Model, Cloud Model, API Keys, Memory Settings, Save). Each section uses a `Grid ColumnDefinitions="140,*"` label+control layout (Memory section uses `200,*`).

**AC-4 (Sprint 2 Day 2):** Three per-provider API key fields (Gemini, Anthropic, OpenAI) were added as `TextBox` with `PasswordChar="*"`. MVVM bindings are correct and null-safe. The ViewModel uses `??= new ProviderKeyConfig()` on write and `?? string.Empty` on read.

**US-4.3 (Sprint 4 Day 4):** Inline validation errors added below DecayLambda, LocalEndpoint, SummarizationInterval, RecentInteractionsFetchLimit. IsDirty dirty tracking with "(unsaved changes)" yellow header. API key warning in yellow semi-transparent border. CanSave = IsDirty && !HasValidationErrors. NumericUpDown ranges: DecayLambda 0.001-1.0, RecentInteractionsFetchLimit max 50.

**US-4.3 review findings:**

HIGH — SaveSettings() method is public but must be private. With `[RelayCommand(CanExecute = nameof(CanSave))]`, callers can bypass the CanSave() guard by invoking the method directly. Change to `private void SaveSettings()`.

MEDIUM — Stale success banner after revert-to-saved: when user edits a field then reverts it to the saved snapshot, IsDirty returns to false. The `!wasDirty && IsDirty` branch in CheckDirty() does not fire (transition is dirty→clean, not clean→dirty), so HasSuccess stays true from the prior save. Fix: in CheckDirty(), also clear HasSuccess/HasError when IsDirty transitions to false (clean state).

MEDIUM — CloudModel remains a free-text TextBox. Should be a ComboBox with AvailableCloudModels collection. Carry-forward from prior review.

MEDIUM — No AutomationProperties.Name on API key TextBox controls. Screen readers cannot associate label with input. Carry-forward from prior review.

MEDIUM — No AutomationProperties.Name on NumericUpDown controls (DecayLambda, SummarizationInterval, RecentInteractionsFetchLimit). Add AutomationProperties.Name matching the label text.

LOW — Validation error TextBlocks have no AutomationProperties.LiveSetting="Polite". Errors will not be announced to screen readers when they appear.

LOW — Inline validation error TextBlock uses FontSize="11". Minimum recommended is 12px for accessibility.

LOW — API key TextBoxes have no Watermark placeholder. Carry-forward.

LOW — No EmbeddingsModel validation — empty string can be saved to config.

**Known persistent gaps:**
- `SaveSettings()` must be `private` (MVVM encapsulation + CanSave guard bypass risk)
- `CloudModel` should be a `ComboBox` (not free-text)
- API key `TextBox` fields need `Watermark` and `AutomationProperties.Name`

**Why:** Findings from UX reviews on 2026-03-15 (AC-4), 2026-03-18 (US-2.2), and 2026-04-04 (US-4.3).
**How to apply:** The SaveSettings() visibility fix is blocking (HIGH). Stale success banner and accessibility attributes are MEDIUM — fix before next UX review. CloudModel ComboBox is a recurring known gap.
