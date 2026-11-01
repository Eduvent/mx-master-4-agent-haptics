// record-event preserves the Python hook's JSONL contract and fail-open output.
package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"time"
	"unicode/utf8"
)

var events = map[string]bool{
	"UserPromptSubmit": true, "PermissionRequest": true, "Stop": true,
	"Interrupt": true, "PreToolUse": true, "PostToolUse": true,
}

func recordEvent(input io.Reader, path string) error {
	data, err := io.ReadAll(input)
	if err != nil {
		return err
	}
	if !utf8.Valid(data) {
		return errors.New("invalid UTF-8 input")
	}
	var payload map[string]interface{}
	if err := json.Unmarshal(data, &payload); err != nil {
		return err
	}
	event, _ := payload["hook_event_name"].(string)
	if !events[event] {
		return errors.New("unsupported hook event")
	}
	record := map[string]interface{}{
		"timestamp": time.Now().UTC().Format("2006-01-02T15:04:05.000000+00:00"),
		"event":     event,
	}
	for _, key := range []string{"session_id", "turn_id", "tool_name", "tool_use_id", "permission_mode"} {
		if value, ok := payload[key].(string); ok {
			record[key] = value
		}
	}
	if message, ok := payload["last_assistant_message"].(string); event == "Stop" && ok {
		record["message_has_question_mark"] = strings.ContainsAny(message, "?¿")
	}
	line, err := json.Marshal(record)
	if err != nil {
		return err
	}
	line = append(line, '\n')
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		return err
	}
	file, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_APPEND, 0666)
	if err != nil {
		return err
	}
	n, writeErr := file.Write(line)
	closeErr := file.Close()
	if writeErr != nil {
		return writeErr
	}
	if n != len(line) {
		return io.ErrShortWrite
	}
	return closeErr
}

func defaultLogPath() (string, error) {
	if path, exists := os.LookupEnv("AGENT_HAPTICS_LOG"); exists {
		return path, nil
	}
	executable, err := os.Executable()
	if err != nil {
		return "", err
	}
	executable, err = filepath.EvalSymlinks(executable)
	if err != nil {
		return "", err
	}
	// Build to <repo>/.local/bin/record-event. Runtime cwd is irrelevant.
	return filepath.Clean(filepath.Join(filepath.Dir(executable), "..", "events.jsonl")), nil
}

func run(input io.Reader, output, diagnostic io.Writer) {
	path, err := defaultLogPath()
	if err == nil {
		err = recordEvent(input, path)
	}
	if err != nil {
		fmt.Fprintln(diagnostic, "agent-haptics: could not record event:", err)
	}
	// Never block the agent, including on invalid payloads or write failures.
	fmt.Fprintln(output, "{}")
}

func main() { run(os.Stdin, os.Stdout, os.Stderr) }
