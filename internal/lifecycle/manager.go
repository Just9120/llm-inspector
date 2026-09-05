package lifecycle

import (
	"context"
	"errors"
	"fmt"
	"maps"
	"slices"
	"strings"
	"sync"
	"time"
	"unicode"
)

// One Manager per backend. op serializes every mutation (including discovery,
// confirmation and parameters); mu keeps Starting/Stopping snapshots readable.
type Manager struct {
	op       sync.Mutex
	mu       sync.RWMutex
	runtime  Runtime
	backend  Backend
	active   func() int
	snapshot Snapshot
}

func NewManager(backend Backend, rt Runtime, active func() int) (*Manager, error) {
	if _, err := Profile(backend); err != nil {
		return nil, err
	}
	if rt == nil || active == nil {
		return nil, ErrTarget
	}
	return &Manager{runtime: rt, backend: backend, active: active, snapshot: Snapshot{State: NotConfigured, Parameters: Defaults(backend)}}, nil
}

func (m *Manager) Snapshot() Snapshot {
	m.mu.RLock()
	s := cloneSnapshot(m.snapshot)
	m.mu.RUnlock()
	s.ActiveRequests = max(0, m.active())
	return s
}
func cloneSnapshot(s Snapshot) Snapshot {
	s.Parameters = maps.Clone(s.Parameters)
	if s.Owned != nil {
		value := *s.Owned
		s.Owned = &value
	}
	if s.Target != nil {
		value := *s.Target
		value.Compatibility.Capabilities = slices.Clone(value.Compatibility.Capabilities)
		value.Compatibility.Windows = slices.Clone(value.Compatibility.Windows)
		value.Compatibility.Evidence = slices.Clone(value.Compatibility.Evidence)
		value.Compatibility.Limitations = slices.Clone(value.Compatibility.Limitations)
		if value.Compatibility.VerifiedAtUTC != nil {
			t := *value.Compatibility.VerifiedAtUTC
			value.Compatibility.VerifiedAtUTC = &t
		}
		s.Target = &value
	}
	return s
}
func (m *Manager) put(s Snapshot) { m.mu.Lock(); m.snapshot = cloneSnapshot(s); m.mu.Unlock() }
func (m *Manager) require(s Snapshot, capability Capability) error {
	if s.Target == nil || !s.Confirmed || s.Target.ConfirmationToken != confirmation(*s.Target) {
		return ErrTarget
	}
	if !slices.Contains(s.Target.Compatibility.Capabilities, capability) {
		return ErrUnsupported
	}
	return nil
}
func (m *Manager) noRequests() error {
	if count := m.active(); count > 0 {
		return fmt.Errorf("%w: %d", ErrBusy, count)
	}
	return nil
}

func (m *Manager) Discover(ctx context.Context, manualPath string) (Snapshot, error) {
	m.op.Lock()
	defer m.op.Unlock()
	s := m.Snapshot()
	if s.Owned != nil {
		return s, ErrOwnership
	}
	path, err := m.runtime.Resolve(ctx, m.backend, manualPath)
	if err != nil {
		return s, err
	}
	result, err := m.runtime.Execute(ctx, Command{Executable: path, Arguments: []string{"--version"}, Timeout: 5 * time.Second})
	if err != nil || result.ExitCode != 0 {
		return s, ErrCommand
	}
	version := ""
	for _, line := range strings.Split(result.Stdout+"\n"+result.Stderr, "\n") {
		line = strings.TrimSpace(line)
		if line != "" {
			if len(line) > 512 || strings.IndexFunc(line, unicode.IsControl) >= 0 {
				return s, ErrCommand
			}
			version = line
			break
		}
	}
	if version == "" {
		return s, ErrCommand
	}
	values := Defaults(m.backend)
	target := Target{Backend: m.backend, Executable: path, Version: version, Endpoint: endpoint(m.backend, values), Compatibility: classify(m.backend, version)}
	target.Compatibility = probeCapabilities(ctx, m.runtime, target)
	target.ConfirmationToken = confirmation(target)
	s = Snapshot{State: PendingConfirmation, Target: &target, Parameters: values}
	m.put(s)
	return m.Snapshot(), nil
}

func (m *Manager) Confirm(token string) error {
	m.op.Lock()
	defer m.op.Unlock()
	s := m.Snapshot()
	if s.Target == nil || token == "" || token != s.Target.ConfirmationToken || token != confirmation(*s.Target) {
		return ErrTarget
	}
	if !slices.Contains(s.Target.Compatibility.Capabilities, Start) {
		return ErrUnsupported
	}
	s.Confirmed = true
	if s.Owned == nil {
		s.State = Stopped
	}
	s.Error = ""
	m.put(s)
	return nil
}

func (m *Manager) SetParameter(id, value string) error {
	return m.SetParameters(map[string]string{id: value})
}

// SetParameters validates the complete edit before changing any field. A port
// change invalidates confirmation once, without causing a partial UI form save.
func (m *Manager) SetParameters(values map[string]string) error {
	m.op.Lock()
	defer m.op.Unlock()
	s := m.Snapshot()
	if err := m.require(s, Parameters); err != nil {
		return err
	}
	if len(values) == 0 || len(values) > len(s.Parameters) {
		return ErrParameter
	}
	next := make(map[string]string, len(s.Parameters))
	for key, value := range s.Parameters {
		next[key] = value
	}
	for key, value := range values {
		normalized, err := Normalize(m.backend, key, value)
		if err != nil {
			return err
		}
		next[key] = normalized
	}
	if next["local-port"] != s.Parameters["local-port"] {
		if s.Owned != nil && m.runtime.Alive(*s.Owned) {
			return ErrOwnership
		}
		s.Target.Endpoint = endpoint(m.backend, next)
		s.Target.ConfirmationToken = confirmation(*s.Target)
		s.Confirmed = false
		s.State = PendingConfirmation
	}
	s.Parameters = next
	if m.backend == LMStudio {
		s.Model = next["model-id"]
	}
	s.Error = ""
	m.put(s)
	return nil
}

func (m *Manager) ResetParameters() error {
	m.op.Lock()
	defer m.op.Unlock()
	s := m.Snapshot()
	if err := m.require(s, Parameters); err != nil {
		return err
	}
	values := Defaults(m.backend)
	if values["local-port"] != s.Parameters["local-port"] {
		if s.Owned != nil && m.runtime.Alive(*s.Owned) {
			return ErrOwnership
		}
		s.Target.Endpoint = endpoint(m.backend, values)
		s.Target.ConfirmationToken = confirmation(*s.Target)
		s.Confirmed = false
		s.State = PendingConfirmation
	}
	s.Parameters = values
	if m.backend == LMStudio {
		s.Model = ""
	}
	s.Error = ""
	m.put(s)
	return nil
}

func (m *Manager) Start(ctx context.Context) error {
	m.op.Lock()
	defer m.op.Unlock()
	return m.start(ctx)
}
func (m *Manager) start(ctx context.Context) error {
	s := m.Snapshot()
	if err := m.require(s, Start); err != nil {
		return err
	}
	if s.Owned != nil && m.runtime.Alive(*s.Owned) {
		return nil
	}
	if s.Owned != nil {
		if err := m.stop(ctx); err != nil {
			return err
		}
		s = m.Snapshot()
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	owner, err := m.runtime.Listener(ctx, s.Target.Endpoint)
	if err != nil {
		return err
	}
	if owner != nil {
		return fmt.Errorf("%w (PID %d)", ErrOccupied, owner.PID)
	}
	if m.backend == LlamaCpp && !m.runtime.FileExists(s.Model) {
		return ErrModel
	}
	plan, err := startPlan(*s.Target, s.Parameters, s.Model)
	if err != nil {
		return err
	}
	s.State = Starting
	s.Error = ""
	s.Owned = nil
	m.put(s)
	identity, err := m.runtime.Start(ctx, plan)
	// Runtime must return retained exact ownership even after a partial failure.
	s.Owned = identity
	m.put(s)
	if err == nil && (identity == nil || !m.runtime.Alive(*identity)) {
		err = ErrOwnership
	}
	if err == nil && !awaitReady(ctx, m.runtime, *s.Target) {
		err = ErrReadiness
	}
	if err == nil && m.backend == LlamaCpp {
		err = confirmModel(ctx, m.runtime, *s.Target, s.Model)
	}
	if err == nil {
		err = m.ownsEndpoint(ctx, s)
	}
	if err == nil {
		s.State = Running
		m.put(s)
		return nil
	}
	return m.failedStart(s, err)
}
func (m *Manager) failedStart(s Snapshot, failure error) error {
	if s.Owned != nil {
		// Caller cancellation must not skip cleanup. The independent bounded
		// context can affect only the identity that this Start returned as owned.
		cleanup, cancel := context.WithTimeout(context.Background(), 30*time.Second)
		err := m.runtime.Stop(cleanup, *s.Owned, officialStop(*s.Target))
		cancel()
		if err == nil {
			s.Owned = nil
		} else {
			failure = errors.Join(failure, ErrOwnership)
		}
	}
	s.State = Faulted
	s.Error = failure.Error()
	m.put(s)
	return failure
}

func (m *Manager) Stop(ctx context.Context) error {
	m.op.Lock()
	defer m.op.Unlock()
	return m.stop(ctx)
}
func (m *Manager) stop(ctx context.Context) error {
	if err := m.noRequests(); err != nil {
		return err
	}
	s := m.Snapshot()
	if err := m.require(s, Stop); err != nil {
		return err
	}
	if s.Owned == nil {
		s.Owned = nil
		s.State = Stopped
		s.Error = ""
		m.put(s)
		return nil
	}
	s.State = Stopping
	s.Error = ""
	m.put(s)
	if err := m.runtime.Stop(ctx, *s.Owned, officialStop(*s.Target)); err != nil {
		s.State = Faulted
		s.Error = err.Error()
		m.put(s)
		return err
	}
	s.Owned = nil
	s.State = Stopped
	s.Error = ""
	m.put(s)
	return nil
}
func (m *Manager) Restart(ctx context.Context) error {
	m.op.Lock()
	defer m.op.Unlock()
	if err := m.require(m.Snapshot(), Restart); err != nil {
		return err
	}
	if err := m.stop(ctx); err != nil {
		return err
	}
	return m.start(ctx)
}

func (m *Manager) Models(ctx context.Context) ([]string, error) {
	m.op.Lock()
	defer m.op.Unlock()
	s := m.Snapshot()
	if err := m.require(s, ModelLoad); err != nil {
		return nil, err
	}
	return listModels(ctx, m.runtime, *s.Target, false)
}
func (m *Manager) LoadModel(ctx context.Context, model string) error {
	m.op.Lock()
	defer m.op.Unlock()
	if err := m.noRequests(); err != nil {
		return err
	}
	s := m.Snapshot()
	if err := m.require(s, ModelLoad); err != nil {
		return err
	}
	if !validModel(model) {
		return ErrModel
	}
	if m.backend == LlamaCpp {
		if !localFile(model, ".gguf") || !m.runtime.FileExists(model) {
			return ErrModel
		}
		wasRunning := s.Owned != nil && m.runtime.Alive(*s.Owned)
		if wasRunning {
			if err := m.stop(ctx); err != nil {
				return err
			}
			s = m.Snapshot()
		}
		s.Model = model
		s.Error = ""
		m.put(s)
		// Selection before first Start is not a successful load claim.
		if !wasRunning {
			return nil
		}
		return m.start(ctx)
	}
	if s.State != Running || s.Owned == nil || !m.runtime.Alive(*s.Owned) {
		return ErrOwnership
	}
	if err := m.ownsEndpoint(ctx, s); err != nil {
		return err
	}
	// Native generate/load must not implicitly select/download an unknown model.
	installed, err := listModels(ctx, m.runtime, *s.Target, false)
	if err != nil || !slices.Contains(installed, model) {
		return ErrModel
	}
	if err = m.ownsEndpoint(ctx, s); err != nil {
		return err
	}
	if err = loadModel(ctx, m.runtime, *s.Target, s.Parameters, model); err != nil {
		s.Error = ErrModel.Error()
		m.put(s)
		return err
	}
	if err = m.ownsEndpoint(ctx, s); err != nil {
		return err
	}
	s.Model = model
	s.Error = ""
	m.put(s)
	return nil
}
func (m *Manager) Refresh() Snapshot {
	// UI refresh must remain readable while a long model load is serialized.
	if !m.op.TryLock() {
		return m.Snapshot()
	}
	defer m.op.Unlock()
	s := m.Snapshot()
	if s.State == Running && s.Owned != nil && !m.runtime.Alive(*s.Owned) {
		s.State = Crashed
		s.Error = "Backend завершился; автоматический перезапуск отключён"
		m.put(s)
	}
	return m.Snapshot()
}

func (m *Manager) ownsEndpoint(ctx context.Context, s Snapshot) error {
	if s.Owned == nil || s.Target == nil || !m.runtime.Alive(*s.Owned) {
		return ErrOwnership
	}
	owner, err := m.runtime.Listener(ctx, s.Target.Endpoint)
	if err != nil || owner == nil || owner.PID != s.Owned.PID || !owner.StartedAt.Equal(s.Owned.StartedAt) || !strings.EqualFold(owner.ImagePath, s.Owned.ImagePath) {
		return ErrOwnership
	}
	return nil
}
