#!/bin/bash

# Post-write validation hook
# Runs after any code file is written
# Validates Dart/Flutter files

INPUT=$(cat)
FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')

# Exit if no file path
if [ -z "$FILE_PATH" ]; then
    echo "{}"
    exit 0
fi

# Only validate Dart files
if [[ "$FILE_PATH" == *.dart ]]; then
    # Log the change
    LOG_DIR="$HOME/.claude"
    mkdir -p "$LOG_DIR"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] Code written: $FILE_PATH" >> "$LOG_DIR/code_changes.log"

    # Optional: Run dart analyze on the file (uncomment if needed)
    # dart analyze "$FILE_PATH" 2>/dev/null

    # Optional: Run dart format on the file (uncomment if needed)
    # dart format "$FILE_PATH" 2>/dev/null
fi

# Return success
echo "{}"
