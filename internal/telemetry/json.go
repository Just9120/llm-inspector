// Package telemetry projects bounded protocol input onto technical metadata.
// Raw request/response content never crosses this package's output boundary.
package telemetry

import (
	"encoding/json"
	"strconv"
	"strings"
)

const MaxTokenBytes = 256
const MaxDepth = 64

type scalar struct {
	path     string
	text     string
	kind     byte
	nonempty bool
}

type frame struct {
	kind  byte
	state int
	path  string
	key   string
	index int
	seen  map[string]bool
}

// jsonProjection is an incremental lexer and grammar recognizer. Even skipped
// strings have bounded memory; a megabyte of content is never decoded as a string.
type jsonProjection struct {
	stack            []frame
	rootDone         bool
	invalid          bool
	mode             byte
	token            [MaxTokenBytes]byte
	length           int
	overflow         bool
	keyToken         bool
	escaped          bool
	unicodeRemaining int
	nonempty         bool
	path             string
	onScalar         func(scalar)
	allowText        func(string) bool
	onArrayEnd       func(string, int)
	onObjectEnd      func(string)
}

func (p *jsonProjection) feed(data []byte) {
	for _, b := range data {
		p.byte(b)
	}
}

func (p *jsonProjection) append(b byte) {
	if p.length < len(p.token) {
		p.token[p.length] = b
		p.length++
	} else {
		p.overflow = true
	}
}

func (p *jsonProjection) valuePath() string {
	if len(p.stack) == 0 {
		return ""
	}
	f := &p.stack[len(p.stack)-1]
	if f.kind == '[' {
		return f.path + "/" + strconv.Itoa(f.index)
	}
	return f.path + "/" + f.key
}

func (p *jsonProjection) wantsValue() bool {
	if len(p.stack) == 0 {
		return !p.rootDone
	}
	f := p.stack[len(p.stack)-1]
	return (f.kind == '{' && f.state == 2) || (f.kind == '[' && (f.state == 0 || f.state == 4))
}

func (p *jsonProjection) valueDone() {
	if len(p.stack) == 0 {
		p.rootDone = true
		return
	}
	f := &p.stack[len(p.stack)-1]
	f.state = 3
	if f.kind == '[' {
		f.index++
	}
}

func (p *jsonProjection) byte(b byte) {
	if p.invalid {
		return
	}
	if p.mode == '"' {
		p.append(b)
		if p.unicodeRemaining > 0 {
			if !strings.ContainsRune("0123456789abcdefABCDEF", rune(b)) {
				p.invalid = true
			}
			p.unicodeRemaining--
			return
		}
		if p.escaped {
			p.escaped = false
			if b == 'u' {
				p.unicodeRemaining = 4
			} else if !strings.ContainsRune("\"\\/bfnrt", rune(b)) {
				p.invalid = true
			}
			p.nonempty = true
			return
		}
		if b == '\\' {
			p.escaped = true
			return
		}
		if b < 0x20 {
			p.invalid = true
			return
		}
		if b != '"' {
			p.nonempty = true
			return
		}
		p.mode = 0
		value := ""
		if !p.overflow && (p.keyToken || (p.allowText != nil && p.allowText(p.path))) {
			if json.Unmarshal(p.token[:p.length], &value) != nil {
				p.invalid = true
				return
			}
		}
		if p.keyToken {
			f := &p.stack[len(p.stack)-1]
			// Keys are never silently truncated or flattened into another path.
			if p.overflow || len(f.seen) >= 64 || f.seen[value] {
				p.invalid = true
				return
			}
			f.seen[value] = true
			if strings.ContainsAny(value, "/\x00") {
				value = "\x00"
			}
			f.key = value
			f.state = 1
		} else {
			p.onScalar(scalar{p.path, value, 's', p.nonempty})
			p.valueDone()
		}
		return
	}
	if p.mode == 'n' {
		if b == ',' || b == ']' || b == '}' || b == ' ' || b == '\n' || b == '\r' || b == '\t' {
			p.finishLiteral()
			p.byte(b)
			return
		}
		p.append(b)
		return
	}
	if b == ' ' || b == '\r' || b == '\n' || b == '\t' {
		return
	}
	if b == '"' {
		p.keyToken = false
		if len(p.stack) > 0 {
			f := p.stack[len(p.stack)-1]
			p.keyToken = f.kind == '{' && (f.state == 0 || f.state == 4)
		}
		if !p.keyToken && !p.wantsValue() {
			p.invalid = true
			return
		}
		p.mode = '"'
		p.length = 0
		p.overflow = false
		p.nonempty = false
		p.path = p.valuePath()
		p.append(b)
		return
	}
	if b == '{' || b == '[' {
		if !p.wantsValue() || len(p.stack) >= MaxDepth {
			p.invalid = true
			return
		}
		p.stack = append(p.stack, frame{kind: b, path: p.valuePath(), seen: map[string]bool{}})
		return
	}
	if b == '}' || b == ']' {
		if len(p.stack) == 0 {
			p.invalid = true
			return
		}
		f := p.stack[len(p.stack)-1]
		if (f.kind == '{' && b != '}') || (f.kind == '[' && b != ']') || (f.state != 0 && f.state != 3) {
			p.invalid = true
			return
		}
		if f.kind == '[' && p.onArrayEnd != nil {
			p.onArrayEnd(f.path, f.index)
		}
		if f.kind == '{' && p.onObjectEnd != nil {
			p.onObjectEnd(f.path)
		}
		p.stack = p.stack[:len(p.stack)-1]
		p.valueDone()
		return
	}
	if b == ':' || b == ',' {
		if len(p.stack) == 0 {
			p.invalid = true
			return
		}
		f := &p.stack[len(p.stack)-1]
		if b == ':' && f.kind == '{' && f.state == 1 {
			f.state = 2
			return
		}
		if b == ',' && f.state == 3 {
			f.state = 4
			return
		}
		p.invalid = true
		return
	}
	if !p.wantsValue() {
		p.invalid = true
		return
	}
	p.mode = 'n'
	p.length = 0
	p.overflow = false
	p.path = p.valuePath()
	p.append(b)
}

func (p *jsonProjection) finishLiteral() {
	p.mode = 0
	if p.overflow || !json.Valid(p.token[:p.length]) {
		p.invalid = true
		return
	}
	v := string(p.token[:p.length])
	if strings.ContainsAny(v, "\"{}[]") {
		p.invalid = true
		return
	}
	p.onScalar(scalar{path: p.path, text: v, kind: 'n'})
	p.valueDone()
}

func (p *jsonProjection) complete() bool {
	if p.mode == 'n' {
		p.finishLiteral()
	}
	return !p.invalid && p.mode == 0 && p.rootDone && len(p.stack) == 0
}
