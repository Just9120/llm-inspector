package remote

import (
	"context"
	"encoding/json"
	"errors"
	"strings"
	"sync"
	"testing"
)

type memoryCredentials struct {
	value            Stored
	loadErr, saveErr error
	saves            int
}

func (s *memoryCredentials) Load(context.Context) (Stored, error) {
	v := s.value
	v.Token = append([]byte(nil), v.Token...)
	return v, s.loadErr
}
func (s *memoryCredentials) Save(_ context.Context, v Stored) error {
	s.saves++
	if s.saveErr != nil {
		return s.saveErr
	}
	s.value = v
	s.value.Token = append([]byte(nil), v.Token...)
	return nil
}
func TestRemoteDefaultConfirmationOneTimeRotationAndRevocation(t *testing.T) {
	store := &memoryCredentials{}
	m := NewManager(store)
	ctx := t.Context()
	defer m.Close()
	if m.Enabled() || m.IsBearerTokenValid(strings.Repeat("a", 43)) {
		t.Fatal("default ingress enabled")
	}
	if _, err := m.Enable(ctx, true); !errors.Is(err, ErrUnavailable) {
		t.Fatal("uninitialized store")
	}
	if err := m.Initialize(ctx); err != nil || m.Enabled() || !m.Snapshot().Available {
		t.Fatal(err)
	}
	if _, err := m.Enable(ctx, false); !errors.Is(err, ErrConfirmation) || store.saves != 0 {
		t.Fatal("missing boundary confirmation")
	}
	if _, err := m.Rotate(ctx, true); err == nil {
		t.Fatal("rotated disabled credential")
	}
	enabled, err := m.Enable(ctx, true)
	if err != nil || enabled.OneTimeToken == nil || len(*enabled.OneTimeToken) != 43 || !m.IsBearerTokenValid(*enabled.OneTimeToken) {
		t.Fatal("creation")
	}
	repeated, err := m.Enable(ctx, true)
	if err != nil || repeated.OneTimeToken != nil || store.saves != 1 {
		t.Fatal("one-time token exposed again")
	}
	snapshot := m.Snapshot()
	snapshot.UpdatedAt = nil
	encoded, _ := json.Marshal(m.Snapshot())
	if strings.Contains(string(encoded), *enabled.OneTimeToken) {
		t.Fatal("secret in status")
	}
	previousBuffer := m.token
	rotated, err := m.Rotate(ctx, true)
	if err != nil || *rotated.OneTimeToken == *enabled.OneTimeToken || m.IsBearerTokenValid(*enabled.OneTimeToken) || !m.IsBearerTokenValid(*rotated.OneTimeToken) {
		t.Fatal("rotation")
	}
	for _, v := range previousBuffer {
		if v != 0 {
			t.Fatal("old secret buffer not cleared")
		}
	}
	if _, err = m.Disable(ctx); err != nil || m.Enabled() || m.IsBearerTokenValid(*rotated.OneTimeToken) || store.value.Token != nil || m.Snapshot().HasCredential {
		t.Fatal("revocation")
	}
}
func TestRemoteFailedSaveDoesNotPublishNewStateOrLoseOldToken(t *testing.T) {
	store := &memoryCredentials{}
	m := NewManager(store)
	m.Initialize(t.Context())
	enabled, _ := m.Enable(t.Context(), true)
	store.saveErr = errors.New("disk full")
	if _, err := m.Rotate(t.Context(), true); err == nil || !m.IsBearerTokenValid(*enabled.OneTimeToken) {
		t.Fatal("failed rotation changed state")
	}
	if _, err := m.Disable(t.Context()); err == nil || !m.Enabled() {
		t.Fatal("failed disable claimed success")
	}
	m.Close()
	if m.Enabled() || m.IsBearerTokenValid(*enabled.OneTimeToken) {
		t.Fatal("closed credential")
	}
	if m.Initialize(t.Context()) == nil {
		t.Fatal("closed restart")
	}
}
func TestRemoteInvalidStoreFailClosedAndStrictTokenEncoding(t *testing.T) {
	for _, s := range []Stored{{Enabled: true}, {Token: make([]byte, 31)}, {Token: make([]byte, 33)}} {
		m := NewManager(&memoryCredentials{value: s})
		if m.Initialize(t.Context()) == nil || m.Enabled() || m.Snapshot().Available {
			t.Fatal("bad store accepted")
		}
	}
	store := &memoryCredentials{}
	m := NewManager(store)
	m.Initialize(t.Context())
	change, _ := m.Enable(t.Context(), true)
	token := *change.OneTimeToken
	for _, bad := range []string{"", token + "=", token[:42], " " + token, strings.Repeat("/", 43), strings.Repeat("-", 10000)} {
		if m.IsBearerTokenValid(bad) {
			t.Fatal("noncanonical token accepted")
		}
	}
	store.loadErr = errors.New("DPAPI user mismatch")
	if m.Initialize(t.Context()) == nil || m.Enabled() || m.IsBearerTokenValid(token) {
		t.Fatal("stale authorization retained")
	}
}
func TestRemoteAuthorizationAndRotationConcurrent(t *testing.T) {
	m := NewManager(&memoryCredentials{})
	m.Initialize(t.Context())
	initial, _ := m.Enable(t.Context(), true)
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := 0; j < 200; j++ {
				m.IsBearerTokenValid(*initial.OneTimeToken)
				m.Snapshot()
			}
		}()
	}
	for i := 0; i < 25; i++ {
		if _, err := m.Rotate(t.Context(), true); err != nil {
			t.Fatal(err)
		}
	}
	wg.Wait()
	if m.IsBearerTokenValid(*initial.OneTimeToken) {
		t.Fatal("stale token")
	}
	m.Close()
}
