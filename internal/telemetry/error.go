package telemetry

// ErrorSession reads only machine error codes, never a free-form error message.
type ErrorSession struct {
	parser          jsonProjection
	contextOverflow bool
}

func NewErrorSession() *ErrorSession {
	s := &ErrorSession{}
	s.parser = jsonProjection{allowText: func(path string) bool { return path == "/error/code" || path == "/error/type" }, onScalar: func(v scalar) {
		if (v.path == "/error/code" || v.path == "/error/type") && v.kind == 's' {
			switch v.text {
			case "context_length_exceeded", "context_window_exceeded", "context_overflow":
				s.contextOverflow = true
			}
		}
	}}
	return s
}
func (s *ErrorSession) Observe(data []byte)   { s.parser.feed(data) }
func (s *ErrorSession) ContextOverflow() bool { return s.parser.complete() && s.contextOverflow }
