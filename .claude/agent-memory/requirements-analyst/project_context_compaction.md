---
name: Context Window Compaction Feature
description: Technical requirements for context window compaction - ContextWindowManager, config changes, AgentService integration
type: project
---

Context window compaction feature analyzed and documented in `docs/technical-requirements/TR-context-window-compaction.md`.

**Why:** `_conversationHistory` in AgentService grows without limit. Local models (8K-32K context) get truncated responses with no warning.

**How to apply:**
- ContextWindowManager goes in Nexus.Core.Services (NOT Memory -- depends on Core config/services)
- Reuses IInteractionSummarizer for summarization (already has LLM + heuristic fallback)
- AgentService constructor gains required ContextWindowManager param (not optional)
- 4 integration points: ChatAsync (before first LLM + inside tool loop), ChatStreamAsync (same 2 points)
- SummarizeAsync side-effect (persists to interactions table) is acceptable -- summary is a valid interaction record
- No mocking library in test project -- must add NSubstitute or hand-roll stubs for IInteractionSummarizer
