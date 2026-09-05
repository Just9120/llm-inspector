package telemetry

import (
	"sort"
	"strconv"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MaxToolsPerTurn = 256
const agentVersion = "openai-agent-metadata-v1"

// RequestSession counts schema entries and the trailing run of tool-result roles.
// It never decodes messages, tool descriptions, schemas, arguments or results.
type RequestSession struct {
	parser     jsonProjection
	available  int
	results    int
	roleTool   bool
	rootObject bool
	closed     bool
	valid      bool
}

func messagePath(path string) bool {
	p := strings.Split(path, "/")
	if len(p) != 3 || p[1] != "messages" {
		return false
	}
	_, e := strconv.Atoi(p[2])
	return e == nil
}
func rolePath(path string) bool {
	return strings.HasSuffix(path, "/role") && messagePath(strings.TrimSuffix(path, "/role"))
}

func NewRequestSession() *RequestSession {
	s := &RequestSession{}
	s.parser = jsonProjection{
		allowText: rolePath,
		onScalar: func(v scalar) {
			if rolePath(v.path) {
				s.roleTool = v.kind == 's' && v.text == "tool"
			}
			if messagePath(v.path) {
				s.results = 0
				s.roleTool = false
			}
		},
		onObjectEnd: func(path string) {
			if path == "" {
				s.rootObject = true
			}
			if messagePath(path) {
				if s.roleTool {
					s.results++
				} else {
					s.results = 0
				}
				s.roleTool = false
			}
		},
		onArrayEnd: func(path string, count int) {
			if path == "/tools" {
				s.available = count
			}
			if messagePath(path) {
				s.results = 0
				s.roleTool = false
			}
		},
	}
	return s
}

func (s *RequestSession) Observe(data []byte) {
	if !s.closed {
		s.parser.feed(data)
	}
}
func (s *RequestSession) Complete(fullyRead bool) (domain.Metric, *int) {
	if !s.closed {
		s.valid = fullyRead && s.parser.complete() && s.rootObject
		s.closed = true
	}
	if !s.valid {
		return domain.Missing(domain.Count, "inspector", agentVersion), nil
	}
	results := s.results
	return domain.Measured(float64(s.available), domain.Count, "inspector", agentVersion), &results
}

type toolFragment struct {
	index   int
	name    string
	invalid bool
}
type agentFragment struct {
	recognized bool
	ambiguous  bool
	completion string
	tools      map[int]*toolFragment
}
type agentAccumulator struct {
	recognized   bool
	corrupt      bool
	completion   string
	names        map[int]string
	invalidNames map[int]bool
}

func toolPath(path string) (position int, field string, ok bool) {
	p := strings.Split(path, "/")
	if len(p) < 6 || p[1] != "choices" || p[2] != "0" || (p[3] != "message" && p[3] != "delta") || p[4] != "tool_calls" {
		return
	}
	position, err := strconv.Atoi(p[5])
	if err != nil || position < 0 {
		return 0, "", false
	}
	return position, strings.Join(p[6:], "/"), true
}

func agentTextPath(path string) bool {
	if path == "/choices/0/finish_reason" {
		return true
	}
	_, field, ok := toolPath(path)
	return ok && field == "function/name"
}

func (a *agentFragment) tool(position int) *toolFragment {
	if position >= MaxToolsPerTurn {
		a.ambiguous = true
		return nil
	}
	if a.tools == nil {
		a.tools = map[int]*toolFragment{}
	}
	if a.tools[position] == nil {
		a.tools[position] = &toolFragment{index: position}
	}
	return a.tools[position]
}

func (a *agentFragment) scalar(v scalar) {
	if v.path == "/choices/0/finish_reason" && v.kind == 's' {
		switch v.text {
		case "tool_calls":
			a.completion = "tool_calls"
		case "stop", "length", "content_filter":
			a.completion = "final"
		default:
			a.completion = "unavailable"
		}
	}
	position, field, ok := toolPath(v.path)
	if !ok {
		return
	}
	tool := a.tool(position)
	if tool == nil {
		return
	}
	switch field {
	case "index":
		n, err := strconv.Atoi(v.text)
		if v.kind != 'n' || err != nil || n < 0 || n >= MaxToolsPerTurn {
			a.ambiguous = true
		} else {
			tool.index = n
		}
	case "function/name":
		if v.kind != 's' || (v.nonempty && v.text == "") || len(v.text) > 128 {
			tool.invalid = true
		} else {
			tool.name = v.text
		}
	case "":
		a.ambiguous = true // tool-call array entry must be an object
	}
}

func (a *agentFragment) objectEnd(path string) {
	if position, field, ok := toolPath(path); ok && field == "" {
		a.tool(position)
	}
}
func (a *agentFragment) arrayEnd(path string, count int) {
	if path == "/choices" {
		a.recognized = true
		if count > 1 {
			a.ambiguous = true
		}
	}
	if path == "/choices/0/message/tool_calls" || path == "/choices/0/delta/tool_calls" {
		if count > MaxToolsPerTurn {
			a.ambiguous = true
		}
	}
}

func (a *agentAccumulator) merge(f agentFragment) {
	a.recognized = a.recognized || f.recognized
	a.corrupt = a.corrupt || f.ambiguous
	if f.completion != "" {
		a.completion = f.completion
	}
	if a.names == nil {
		a.names = map[int]string{}
		a.invalidNames = map[int]bool{}
	}
	seen := map[int]bool{}
	for _, tool := range f.tools {
		if seen[tool.index] {
			a.corrupt = true
		}
		seen[tool.index] = true
		current := a.names[tool.index]
		if tool.invalid || len(current)+len(tool.name) > 128 {
			a.invalidNames[tool.index] = true
		}
		if !a.invalidNames[tool.index] {
			current += tool.name
		}
		a.names[tool.index] = current
	}
}

func (a *agentAccumulator) result(streaming bool) domain.AgentTurn {
	result := domain.MissingAgentTurn()
	if !a.recognized || a.corrupt || (streaming && (a.completion == "" || a.completion == "unavailable")) {
		return result
	}
	result.InvokedTools = domain.Measured(float64(len(a.names)), domain.Count, "inspector", agentVersion)
	result.Completion = a.completion
	if result.Completion == "" {
		result.Completion = "unavailable"
	}
	result.DetailsComplete = true
	for index, name := range a.names {
		name = domain.TechnicalIdentifier(name)
		if name == "" || a.invalidNames[index] {
			result.DetailsComplete = false
			continue
		}
		result.Tools = append(result.Tools, domain.ToolCall{Sequence: index, Name: name})
	}
	sort.Slice(result.Tools, func(i, j int) bool { return result.Tools[i].Sequence < result.Tools[j].Sequence })
	return result
}

func (s *Session) AgentResponse() domain.AgentTurn {
	if s.native {
		return domain.MissingAgentTurn()
	}
	s.Complete()
	return s.agent.result(s.sse)
}
