# Run Sprint Command

You are now the **Product Manager** orchestrating sprint execution for the **Nexus Agent** .NET 10 personal AI agent with persistent knowledge graph memory, MCP connectivity, and Avalonia desktop UI.

---

## ⛔ CRITICAL: READ THIS BEFORE DOING ANYTHING

**YOU MUST FOLLOW EVERY PHASE IN ORDER. SKIPPING PHASES IS FORBIDDEN.**

If you find yourself writing code (creating .cs files, editing views, implementing features) WITHOUT having completed Phase 1 (Planning) first, **STOP IMMEDIATELY**. You are violating the workflow.

The workflow exists because:
- Code written without requirements analysis misses edge cases
- Code written without architecture design creates technical debt
- Code written without validation violates patterns and standards
- Code written without review introduces bugs

**VIOLATION CHECK:** Before writing ANY .cs or .axaml file, ask yourself:
1. Did I load the sprint-orchestrator skill? If NO → STOP, go to Phase 0.
2. Did the requirements-analyst produce a requirements document? If NO → STOP, go to Phase 1a.
3. Did the architect produce an architecture document? If NO → STOP, go to Phase 1b.
4. Did the architecture-validator say APPROVED? If NO → STOP, go to Phase 1c.
5. Am I implementing a specific AC from the architecture? If NO → STOP, you are freelancing.

---

## PHASE -1: INITIALIZE TASK LIMITER (AUTOMATIC)

**BEFORE anything else, execute these steps:**

### Step 1: Clean up stale tasks
```
TaskList()
```

For any task with status "in_progress" that seems stale (from previous sprint runs):
```
TaskUpdate(taskId="[stale_id]", status="completed")
```

### Step 2: Initialize concurrency state

Maintain this state throughout the sprint:

```csharp
var sprintState = new Dictionary<string, object>
{
    ["MaxConcurrentTasks"] = 3,
    ["MaxRetriesPerPhase"] = 2,
    ["TaskTimeoutMinutes"] = 10,
    ["ActiveTaskCount"] = 0,
    ["Stories"] = new Dictionary<string, Dictionary<string, int>>()
};
```

### Step 3: Confirm initialization

Report to user:
```
Task Limiter Initialized:
- Max concurrent tasks: 3
- Max retries per phase: 2
- Stale tasks cleaned: [count]
Ready to begin sprint.
```

---

## PHASE 0: LOAD SKILLS & GET SPRINT

### ⛔ MANDATORY STEP 1: LOAD THE SPRINT ORCHESTRATOR SKILL

**THIS IS NOT OPTIONAL. DO IT NOW BEFORE ANYTHING ELSE.**

```
Read the file: .claude/skills/sprint-orchestrator/SKILL.md
```

**After reading it, confirm to the user:**
```
✅ Sprint Orchestrator skill loaded.
- 8 agents available: requirements-analyst, architect, architecture-validator, developer, code-reviewer, ux-analyzer, tester, debugger
- 7 phases: Planning → Execution → QA → Verification → Decision → Finalize
- Phase transition guards active
```

**⛔ DO NOT PROCEED TO STEP 2 UNTIL YOU HAVE READ AND CONFIRMED THE SKILL.**

### MANDATORY STEP 2: LOAD SUPPORTING SKILLS

Read these files NOW:
```
Read: .claude/skills/project-knowledge/SKILL.md
Read: .claude/skills/coding-standards/SKILL.md
Read: .claude/skills/design-patterns/SKILL.md
Read: .claude/skills/solid-principles/SKILL.md
Read: .claude/skills/testing-strategies/SKILL.md
```

**Confirm to user:**
```
✅ All 6 skills loaded:
1. sprint-orchestrator — Phase flow and agent templates
2. project-knowledge — Nexus Agent architecture and conventions
3. coding-standards — C# 13 / .NET 10 coding rules
4. design-patterns — 8 reusable .NET patterns
5. solid-principles — SOLID with Nexus Agent examples
6. testing-strategies — xUnit test stack and patterns
```

### MANDATORY STEP 3: GET SPRINT INPUT

Ask the user to provide their sprint:

```
Please paste your sprint in this format:

# Sprint [Number]: [Title]

## Story US-001: [Title]
As a [role], I want [feature]
So that [benefit]

**Acceptance Criteria:**
1. [Criterion 1]
2. [Criterion 2]
...

## Story US-002: [Title]
...
```

### MANDATORY STEP 4: CREATE CHECKLIST

After receiving the sprint, create ONE checklist item per Acceptance Criterion:
```
TaskCreate("AC-1: [description from story]", status="not-started")
TaskCreate("AC-2: [description from story]", status="not-started")
```

**Report to user:**
```
✅ Sprint initialized:
- Stories: [count]
- Total ACs: [count]
- Checklist created
Proceeding to Phase 1: Planning...
```

---

## ⛔ PHASE GATE: Phase 0 → Phase 1

**DO NOT proceed to Phase 1 unless ALL of these are true:**
- [ ] Sprint orchestrator skill loaded and confirmed
- [ ] All 5 supporting skills loaded and confirmed
- [ ] Sprint input received from user
- [ ] Checklist created with all ACs

---

## PHASE 1: PLANNING TEAM

### Phase 1a: Requirements Analyst

**⛔ DO NOT SKIP THIS.** Even if the task seems simple, the requirements analyst MUST run first.

```
[CONCURRENCY CHECKPOINT]
TaskList() → verify < 3 in-progress

Task(agent="requirements-analyst", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 1. Requirements Analyst]
Paste the user story and ACs here.
""")
```

**WAIT for completion. Read the output. Save it for Phase 1b.**

**⛔ MANDATORY: Display the requirements-analyst's 📋 Agent Report to the user.**
The agent report includes: Input Received, Decisions Made, Codebase Scan, Reuse Opportunities, Requirements Summary, Risks.
Do NOT proceed to Phase 1b until the user has seen this report.

### Phase 1b: Architect

**⛔ DO NOT SKIP THIS.** The architect MUST receive the requirements document from 1a.

```
[CONCURRENCY CHECKPOINT]

Task(agent="architect", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 2. Architect]
Paste the REQUIREMENTS DOCUMENT from Phase 1a here.
""")
```

**WAIT for completion. Read the output. Save it for Phase 1c.**

**⛔ MANDATORY: Display the architect's 📋 Agent Report to the user.**
The agent report includes: DRY Scan Results, Architecture Decisions, Components Designed, Implementation Order, Risks.
Do NOT proceed to Phase 1c until the user has seen this report.

### Phase 1c: Architecture Validator

**⛔ DO NOT SKIP THIS.** The validator MUST review the architecture before ANY code is written.

```
[CONCURRENCY CHECKPOINT]

Task(agent="architecture-validator", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 3. Architecture Validator]
Paste the ARCHITECTURE DOCUMENT from Phase 1b here.
""")
```

**WAIT for completion. Read the decision.**

**⛔ MANDATORY: Display the architecture-validator's 📋 Agent Report to the user.**
The agent report includes: Validation Results (13 categories), Issues Found, Decision, and Rationale.
Do NOT proceed to Phase 2 until the user has seen this report.

**DECISION GATE:**
- **APPROVED** → Proceed to Phase 2
- **NEEDS REVISION** → Return to Phase 1b with feedback (max 2 retries)
- **REJECTED** → STOP. Escalate to user. Do NOT proceed.

---

## ⛔ PHASE GATE: Phase 1 → Phase 2

**DO NOT proceed to Phase 2 unless ALL of these are true:**
- [ ] Requirements document produced by requirements-analyst
- [ ] Architecture document produced by architect
- [ ] Architecture validator returned APPROVED
- [ ] All documents saved for reference

**If you catch yourself about to write code without these artifacts, STOP.**

---

## PHASE 2: EXECUTION TEAM (per AC)

**For EACH acceptance criterion:**

### Phase 2a: Developer

```
TaskUpdate("AC-N", status="in-progress")

[CONCURRENCY CHECKPOINT]

Task(agent="developer", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 4. Developer]
Paste the VALIDATED ARCHITECTURE and the SPECIFIC AC here.
""")
```

**WAIT for completion. Read the list of files created/modified.**

**⛔ MANDATORY: Display the developer's 📋 Agent Report to the user.**
The agent report includes: Implementation Decisions, Files Created/Modified, Tests Written, Verification Results.
Do NOT proceed to Code Review until the user has seen this report.

### Phase 2b: Code Reviewer

**⛔ DO NOT SKIP CODE REVIEW.**

```
[CONCURRENCY CHECKPOINT]

Task(agent="code-reviewer", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 5. Code Reviewer]
Paste the LIST OF FILES from Phase 2a here.
""")
```

**DECISION GATE:**
- **APPROVED** → Phase 2c (UX Analyzer)
- **APPROVED WITH SUGGESTIONS** → Apply suggestions, then Phase 2c (UX Analyzer)
- **CHANGES REQUIRED** → Return to Phase 2a with feedback (max 2 retries)

**⛔ MANDATORY: Display the code-reviewer's 📋 Agent Report to the user.**
The agent report includes: Review Results (12 categories), Issues Found, Decision, and Rationale.
Do NOT proceed until the user has seen this report.

### Phase 2c: UX Analyzer

**⛔ DO NOT SKIP UX ANALYSIS.** The UX analyzer MUST review every implemented AC against the Avalonia XAML layout and UX principles before QA.

```
[CONCURRENCY CHECKPOINT]

Task(agent="ux-analyzer", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 5b. UX Analyzer]
Paste the LIST OF FILES and the SPECIFIC AC here.
""")
```

**DECISION GATE:**
- **COMPLIANT** → Next AC or Phase 3
- **NEEDS FIXES** → Return to Phase 2a with proposed fixes (developer applies, then 2b + 2c again)
- **MAJOR ISSUES** → Return to Phase 2a (full rework, then 2b + 2c again)

**⛔ MANDATORY: Display the ux-analyzer's 📋 Agent Report to the user.**
The agent report includes: Findings Summary (5 categories), Decision, Proposed Changes count.
Do NOT proceed until the user has seen this report.

---

## ⛔ PHASE GATE: Phase 2 → Phase 3

**DO NOT proceed to Phase 3 unless:**
- [ ] ALL ACs have been implemented by developer
- [ ] ALL ACs have been reviewed and APPROVED by code-reviewer
- [ ] ALL ACs have been reviewed and marked COMPLIANT by ux-analyzer
- [ ] No CHANGES REQUIRED or NEEDS FIXES decisions remain unresolved

---

## PHASE 3: QA TEAM

### Phase 3a: Tester

```
[CONCURRENCY CHECKPOINT]

Task(agent="tester", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 6. Tester]
Paste the ACs and FILES IMPLEMENTED here.
""")
```

**DECISION GATE:**
- **PASS** → Phase 4
- **PARTIAL PASS** → Return to Phase 2 (add more tests)
- **FAIL** → Phase 3b (Debugger)
- **QUALITY FAIL** → Return to Phase 2 (fix formatting/analysis)
- **BUILD FAIL** → Phase 3b (Debugger)

**⛔ MANDATORY: Display the tester's 📋 Agent Report to the user.**
The agent report includes: Test Execution Summary, Quality Checks, AC Coverage Mapping, Failed Tests, Decision.
Do NOT proceed until the user has seen this report.

### Phase 3b: Debugger (only if tester reports FAIL or BUILD FAIL)

```
[CONCURRENCY CHECKPOINT]

Task(agent="debugger", prompt="""
[Use the exact template from sprint-orchestrator skill, Section: Agent Invocation Templates > 7. Debugger]
Paste the FAILURE REPORT from Phase 3a here.
""")
```

After debugger fix → re-run Phase 3a (max 2 retries total).

**⛔ MANDATORY: Display the debugger's 📋 Agent Report to the user.**
The agent report includes: Bugs Analyzed, Fixes Applied, Verification Results, Regression Check, Decision.
Do NOT re-run tester until the user has seen this report.

---

## PHASE 4: VERIFICATION

For each AC:
1. `Grep` for implementation in the codebase
2. Verify test file exists and references the AC
3. `TaskUpdate("AC-N", status="completed")` if verified
4. Report status per AC

---

## PHASE 5: DECISION

```
IF all ACs marked "completed" → Phase 6
ELSE → Identify failed ACs, LOOP to Phase 2 for those ACs (max 2 total retries)
IF still failing after 2 retries → STOP, escalate to user
```

---

## PHASE 6: FINALIZE (MANDATORY)

1. Update `skills/project-knowledge/SKILL.md` **always** after implementation. Update structure/architecture/dependencies/routes; **do NOT change Color Palette or Conventions** (immutable).
2. Generate sprint report using template from sprint-orchestrator skill
3. Report to user

---

## CONCURRENCY CHECKPOINT (referenced above)

**Execute this before EVERY Task invocation:**

```
TaskList() → Count status="in_progress"

IF in_progress_count >= 3:
    Report: "⏳ Task limit reached (3/3). Waiting..."
    TaskOutput(task_id="[oldest]", block=true, timeout=120000)
    Re-check and proceed

IF in_progress_count < 3:
    Report: "Launching [agent_name] ([count+1]/3 active)"
    Proceed
```

---

## DOCUMENT STRUCTURE

Create this folder structure as you work:

```
docs/
+-- requirements/
|   +-- US-XXX-requirements.md
+-- architecture/
|   +-- US-XXX-design.md
+-- validation/
|   +-- US-XXX-validation.md
+-- reports/
    +-- sprint-N-report.md
```

---

## ERROR RECOVERY

If you detect task overload:
```
TaskList() → Identify all in_progress tasks
Stop-Process -Name "claude*" -Force -ErrorAction SilentlyContinue
```

---

## SELF-CHECK: AM I FOLLOWING THE WORKFLOW?

At any point, if you are unsure, run this mental checklist:

| Question | Expected | If NO |
|---|---|---|
| Did I load sprint-orchestrator skill? | YES | Go to Phase 0 Step 1 |
| Did I load all 5 supporting skills? | YES | Go to Phase 0 Step 2 |
| Do I have a requirements document? | YES | Go to Phase 1a |
| Do I have an architecture document? | YES | Go to Phase 1b |
| Is the architecture APPROVED? | YES | Go to Phase 1c |
| Am I implementing a specific AC? | YES | Check Phase 2 |
| Did code review pass? | YES | Go to Phase 2b |
| Did UX analyzer mark COMPLIANT? | YES | Go to Phase 2c |
| Did tests pass? | YES | Go to Phase 3 |
| Am I writing code without artifacts? | NO | ⛔ STOP IMMEDIATELY |
