---
name: Async ViewModel Command Processing Guard
description: Rule for guarding async ViewModel commands that call long-running AgentService methods — IsProcessing must be set before await and reset in finally
type: feedback
---

Any `[RelayCommand]` async method in a ViewModel that awaits an `AgentService` call must bracket the operation with `IsProcessing = true` (before await) and `IsProcessing = false` in a `finally` block. Additionally, the corresponding button in the View must bind `IsEnabled="{Binding !IsProcessing}"` to prevent concurrent command execution.

**Why:** `AgentService.ClearHistoryAsync()` was made async in US-2.2 because it now calls the LLM summarizer before clearing history. Without a processing guard, the "Clear" button gives no feedback during the summarization wait, and the user can click it again or send a message concurrently — creating a race on `_conversationHistory`. Found in UX review on 2026-03-18.

**How to apply:** Every time a new RelayCommand is added that awaits AgentService (ChatAsync, ClearHistoryAsync, FlushPendingExtractionAsync), check that: (1) IsProcessing is set true before the first await, (2) IsProcessing is reset in finally, (3) the triggering button in the View has IsEnabled="{Binding !IsProcessing}".
