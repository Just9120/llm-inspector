// Package lifecycle owns explicit, local-only backend management. Paths and CLI
// output are transient control data, never observation/history/export content.
package lifecycle

import (
	"context"
	"errors"
	"time"
)

type Backend string

const (
	Ollama   Backend = "Ollama"
	LlamaCpp Backend = "LlamaCpp"
	LMStudio Backend = "LmStudio"
)

type State string

const (
	NotConfigured       State = "not_configured"
	PendingConfirmation State = "pending_confirmation"
	Stopped             State = "stopped"
	Starting            State = "starting"
	Running             State = "running"
	Stopping            State = "stopping"
	Crashed             State = "crashed"
	Faulted             State = "faulted"
)

var (
	ErrTarget      = errors.New("сначала найдите и подтвердите точный runtime и endpoint")
	ErrUnsupported = errors.New("операция не подтверждена capability contract runtime")
	ErrParameter   = errors.New("недопустимый параметр runtime")
	ErrBusy        = errors.New("операция запрещена: есть активные запросы Inspector")
	ErrOccupied    = errors.New("порт занят: внешний процесс не изменён")
	ErrOwnership   = errors.New("точное владение процессом не подтверждено")
	ErrReadiness   = errors.New("готовность backend не подтверждена")
	ErrModel       = errors.New("точная загруженная модель не подтверждена")
	ErrCommand     = errors.New("команда runtime не выполнена или превысила лимит")
)

type Identity struct {
	PID       uint32    `json:"pid"`
	StartedAt time.Time `json:"startedAt"`
	ImagePath string    `json:"imagePath"`
}

type Command struct {
	Executable  string
	Arguments   []string
	Environment map[string]string
	Timeout     time.Duration
}

type StartPlan struct {
	Command       Command
	Endpoint      string
	Detached      bool
	AllowedImages []string
}

type CommandResult struct {
	Stdout   string
	Stderr   string
	ExitCode int
}

// Runtime is deliberately not exposed as a desktop binding. Only typed Manager
// operations may construct commands; there is no arbitrary command/URL endpoint.
type Runtime interface {
	Resolve(context.Context, Backend, string) (string, error)
	Execute(context.Context, Command) (CommandResult, error)
	Listener(context.Context, string) (*Identity, error)
	Start(context.Context, StartPlan) (*Identity, error)
	Alive(Identity) bool
	Stop(context.Context, Identity, *Command) error
	HTTP(context.Context, string, string, []byte) ([]byte, error)
	FileExists(string) bool
}

type Capability string

const (
	Start      Capability = "start"
	Stop       Capability = "stop"
	Restart    Capability = "restart"
	ModelLoad  Capability = "model-load"
	Parameters Capability = "parameters"
)

type Compatibility struct {
	Backend           Backend      `json:"backend"`
	VersionMatch      string       `json:"versionMatch"`
	Status            string       `json:"status"`
	Capabilities      []Capability `json:"capabilities"`
	Windows           []string     `json:"windows"`
	VerifiedAtUTC     *time.Time   `json:"verifiedAtUtc"`
	InspectorRevision string       `json:"inspectorRevision"`
	Evidence          []string     `json:"evidence"`
	Limitations       []string     `json:"limitations"`
}

type Target struct {
	Backend           Backend       `json:"backend"`
	Executable        string        `json:"executable"`
	Version           string        `json:"version"`
	Endpoint          string        `json:"endpoint"`
	ConfirmationToken string        `json:"confirmationToken"`
	Compatibility     Compatibility `json:"compatibility"`
}

type Snapshot struct {
	State          State             `json:"state"`
	Target         *Target           `json:"target"`
	Confirmed      bool              `json:"confirmed"`
	Parameters     map[string]string `json:"parameters"`
	Owned          *Identity         `json:"owned"`
	Model          string            `json:"model"`
	ActiveRequests int               `json:"activeRequests"`
	Error          string            `json:"error"`
}
