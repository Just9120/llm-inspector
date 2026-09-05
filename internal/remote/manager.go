// Package remote owns opt-in application credentials, never Tailscale itself.
package remote

import (
	"context"
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"sync"
	"time"
)

const TokenBytes = 32

var ErrUnavailable = errors.New("защищённое хранилище remote access недоступно")
var ErrConfirmation = errors.New("подтвердите private HTTPS Serve, user identity, intended ACL и выключенный Funnel")
var ErrConfiguration = errors.New("remote settings повреждены или недоступны текущему Windows user; remote ingress запрещён")

type Stored struct {
	Enabled   bool
	Token     []byte
	UpdatedAt *time.Time
}

func (s Stored) validate() error {
	if s.Token != nil && len(s.Token) != TokenBytes || s.Enabled && s.Token == nil {
		return ErrConfiguration
	}
	return nil
}

type CredentialStore interface {
	Load(context.Context) (Stored, error)
	Save(context.Context, Stored) error
}
type Snapshot struct {
	Available     bool       `json:"available"`
	Enabled       bool       `json:"enabled"`
	HasCredential bool       `json:"has_credential"`
	UpdatedAt     *time.Time `json:"updated_at"`
	Message       string     `json:"message"`
}
type Change struct {
	Snapshot     Snapshot `json:"snapshot"`
	OneTimeToken *string  `json:"one_time_token"`
}
type Manager struct {
	mu       sync.RWMutex
	store    CredentialStore
	snapshot Snapshot
	token    []byte
	closed   bool
}

func NewManager(store CredentialStore) *Manager {
	return &Manager{store: store, snapshot: Snapshot{Message: "Remote access выключен; защищённое хранилище ещё не проверено."}}
}
func (m *Manager) Snapshot() Snapshot {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return cloneSnapshot(m.snapshot)
}
func cloneSnapshot(s Snapshot) Snapshot {
	if s.UpdatedAt != nil {
		v := *s.UpdatedAt
		s.UpdatedAt = &v
	}
	return s
}
func (m *Manager) Initialize(ctx context.Context) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed {
		return ErrUnavailable
	}
	m.replaceToken(nil)
	m.snapshot = Snapshot{Message: "Remote access недоступен; ingress запрещён."}
	s, err := m.store.Load(ctx)
	defer clear(s.Token)
	if err != nil {
		return ErrConfiguration
	}
	if err = s.validate(); err != nil {
		return err
	}
	m.replaceToken(s.Token)
	m.snapshot = Snapshot{Available: true, Enabled: s.Enabled, HasCredential: s.Token != nil, UpdatedAt: s.UpdatedAt, Message: "Remote access выключен."}
	if s.Enabled {
		m.snapshot.Message = "Защищённый remote access включён."
	}
	m.snapshot = cloneSnapshot(m.snapshot)
	return nil
}
func (m *Manager) Enabled() bool {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return !m.closed && m.snapshot.Available && m.snapshot.Enabled
}
func (m *Manager) IsBearerTokenValid(candidate string) bool {
	if len(candidate) != 43 {
		return false
	}
	actual, err := base64.RawURLEncoding.Strict().DecodeString(candidate)
	if err != nil {
		return false
	}
	defer clear(actual)
	m.mu.RLock()
	defer m.mu.RUnlock()
	return !m.closed && m.snapshot.Available && m.snapshot.Enabled && len(actual) == TokenBytes && len(m.token) == TokenBytes && subtle.ConstantTimeCompare(actual, m.token) == 1
}
func (m *Manager) Enable(ctx context.Context, confirmed bool) (Change, error) {
	return m.change(ctx, confirmed, false, false)
}
func (m *Manager) Rotate(ctx context.Context, confirmed bool) (Change, error) {
	return m.change(ctx, confirmed, true, false)
}
func (m *Manager) Disable(ctx context.Context) (Change, error) {
	return m.change(ctx, true, false, true)
}
func (m *Manager) change(ctx context.Context, confirmed, rotate, disable bool) (Change, error) {
	if !confirmed {
		return Change{}, ErrConfirmation
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed || !m.snapshot.Available {
		return Change{}, ErrUnavailable
	}
	if err := ctx.Err(); err != nil {
		return Change{}, err
	}
	if !disable && !rotate && m.snapshot.Enabled {
		return Change{Snapshot: cloneSnapshot(m.snapshot)}, nil
	}
	if rotate && !m.snapshot.Enabled {
		return Change{}, errors.New("сначала включите remote access")
	}
	var token []byte
	if !disable {
		token = make([]byte, TokenBytes)
		if _, err := rand.Read(token); err != nil {
			return Change{}, ErrUnavailable
		}
	}
	defer clear(token)
	now := time.Now().UTC()
	if err := m.store.Save(ctx, Stored{Enabled: !disable, Token: token, UpdatedAt: &now}); err != nil {
		return Change{}, ErrConfiguration
	}
	m.replaceToken(token)
	m.snapshot = Snapshot{Available: true, Enabled: !disable, HasCredential: !disable, UpdatedAt: &now, Message: "Remote access выключен; token отозван."}
	result := Change{}
	if !disable {
		encoded := base64.RawURLEncoding.EncodeToString(token)
		result.OneTimeToken = &encoded
		m.snapshot.Message = "Remote access включён. Новый token показывается только сейчас; предыдущий недействителен."
	}
	result.Snapshot = cloneSnapshot(m.snapshot)
	return result, nil
}
func (m *Manager) replaceToken(token []byte) { clear(m.token); m.token = append([]byte(nil), token...) }
func (m *Manager) Close() {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.closed = true
	m.replaceToken(nil)
	m.snapshot = Snapshot{Message: "Remote access остановлен."}
}
