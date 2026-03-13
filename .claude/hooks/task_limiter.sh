#!/bin/bash

# Task Limiter Hook
# Runs BEFORE any Task tool invocation
# Limits concurrent tasks and cleans up stale ones
#
# Configuration:
MAX_CONCURRENT_TASKS=3
TASK_TIMEOUT_MINUTES=10
LOG_FILE="$HOME/.claude/task_limiter.log"

# Ensure log directory exists
mkdir -p "$(dirname "$LOG_FILE")"

# Read input from Claude Code
INPUT=$(cat)

# Parse the tool being called
TOOL_NAME=$(echo "$INPUT" | jq -r '.tool_name // empty')

# Only intercept Task tool calls
if [ "$TOOL_NAME" != "Task" ]; then
    echo "{}"
    exit 0
fi

# Extract task details
SUBAGENT_TYPE=$(echo "$INPUT" | jq -r '.tool_input.subagent_type // "unknown"')
DESCRIPTION=$(echo "$INPUT" | jq -r '.tool_input.description // "no description"')
RUN_IN_BACKGROUND=$(echo "$INPUT" | jq -r '.tool_input.run_in_background // false')

# Log the attempt
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
echo "[$TIMESTAMP] Task attempt: $SUBAGENT_TYPE - $DESCRIPTION (background: $RUN_IN_BACKGROUND)" >> "$LOG_FILE"

# Function to count active Claude Code tasks
count_active_tasks() {
    # Count background processes related to Claude Code tasks
    local count=$(pgrep -f "claude.*task\|claude.*agent" 2>/dev/null | wc -l | tr -d ' ')
    echo "${count:-0}"
}

# Function to get stale task PIDs (older than TASK_TIMEOUT_MINUTES)
get_stale_task_pids() {
    # Find processes older than timeout
    local timeout_seconds=$((TASK_TIMEOUT_MINUTES * 60))
    local now=$(date +%s)

    for pid in $(pgrep -f "claude.*task\|claude.*agent" 2>/dev/null); do
        local start_time=$(ps -o lstart= -p $pid 2>/dev/null)
        if [ -n "$start_time" ]; then
            local start_epoch=$(date -j -f "%a %b %d %H:%M:%S %Y" "$start_time" +%s 2>/dev/null || echo "0")
            local age=$((now - start_epoch))
            if [ "$age" -gt "$timeout_seconds" ]; then
                echo "$pid"
            fi
        fi
    done
}

# Function to check for duplicate tasks
is_duplicate_task() {
    local agent_type="$1"
    # Check if there's already a running task of the same type
    pgrep -f "subagent_type.*$agent_type" >/dev/null 2>&1
    return $?
}

# Kill stale tasks
kill_stale_tasks() {
    local stale_pids=$(get_stale_task_pids)
    for pid in $stale_pids; do
        echo "[$TIMESTAMP] Killing stale task PID: $pid" >> "$LOG_FILE"
        kill -TERM "$pid" 2>/dev/null
    done
}

# Main logic
ACTIVE_COUNT=$(count_active_tasks)
echo "[$TIMESTAMP] Active tasks: $ACTIVE_COUNT (max: $MAX_CONCURRENT_TASKS)" >> "$LOG_FILE"

# Kill stale tasks first
kill_stale_tasks

# Re-count after cleanup
ACTIVE_COUNT=$(count_active_tasks)

# Check if we're at the limit
if [ "$ACTIVE_COUNT" -ge "$MAX_CONCURRENT_TASKS" ]; then
    # Block the task with a message
    echo "[$TIMESTAMP] BLOCKED: Task limit reached ($ACTIVE_COUNT >= $MAX_CONCURRENT_TASKS)" >> "$LOG_FILE"

    # Return a block response
    cat << EOF
{
    "decision": "block",
    "reason": "Task limit reached: $ACTIVE_COUNT active tasks (max: $MAX_CONCURRENT_TASKS). Please wait for existing tasks to complete before launching new ones. Consider using TaskList to check status and TaskStop to clean up stale tasks."
}
EOF
    exit 0
fi

# Allow the task
echo "[$TIMESTAMP] ALLOWED: Task $SUBAGENT_TYPE ($ACTIVE_COUNT < $MAX_CONCURRENT_TASKS)" >> "$LOG_FILE"

# Return empty (allow)
echo "{}"
