package remote

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
)

type fixtureProtector struct{ fail bool }

func (p fixtureProtector) Protect(input []byte) ([]byte, error) {
	if p.fail {
		return nil, errors.New("protected storage failure")
	}
	out := make([]byte, len(input))
	for i, v := range input {
		out[i] = v ^ 0xa5
	}
	return out, nil
}
func (p fixtureProtector) Unprotect(input []byte) ([]byte, error) { return p.Protect(input) }
func TestRemoteFileSchemaAtomicReplacementAndNoPlaintext(t *testing.T) {
	path := filepath.Join(t.TempDir(), "remote-access.json")
	store, err := NewFileStore(path, fixtureProtector{})
	if err != nil {
		t.Fatal(err)
	}
	v, err := store.Load(t.Context())
	if err != nil || v.Enabled || v.Token != nil {
		t.Fatal("default store")
	}
	token := bytes.Repeat([]byte{17}, 32)
	at := time.Date(2026, 9, 5, 8, 0, 0, 0, time.UTC)
	if err = store.Save(t.Context(), Stored{true, token, &at}); err != nil {
		t.Fatal(err)
	}
	data, _ := os.ReadFile(path)
	if bytes.Contains(data, []byte(base64.StdEncoding.EncodeToString(token))) || bytes.Contains(data, token) {
		t.Fatal("plaintext credential persisted")
	}
	var fields map[string]json.RawMessage
	if json.Unmarshal(data, &fields) != nil || len(fields) != 4 {
		t.Fatal("file allowlist")
	}
	v, err = store.Load(t.Context())
	if err != nil || !bytes.Equal(v.Token, token) || !v.UpdatedAt.Equal(at) {
		t.Fatal("roundtrip", err)
	}
	if err = store.Save(t.Context(), Stored{UpdatedAt: &at}); err != nil {
		t.Fatal(err)
	}
	v, err = store.Load(t.Context())
	if err != nil || v.Token != nil || v.Enabled {
		t.Fatal("disable persistence")
	}
	entries, _ := os.ReadDir(filepath.Dir(path))
	if len(entries) != 1 {
		t.Fatal("temporary files leaked")
	}
}
func TestRemoteUnknownCorruptOrOtherUserFileNeverOverwritten(t *testing.T) {
	for _, input := range []string{`null`, `{`, `{"schema_version":2}`, `{"schema_version":1,"enabled":true}`, `{"schema_version":1,"protected_bearer_token":"!"}`, `{"schema_version":1,"protected_bearer_token":""}`, `{"schema_version":1,"unknown":"x"}`, `{"schema_version":1} {}`, `{"schema_version":1,"protected_bearer_token":"AA=="}`} {
		path := filepath.Join(t.TempDir(), "remote-access.json")
		os.WriteFile(path, []byte(input), 0600)
		store, _ := NewFileStore(path, fixtureProtector{})
		if _, err := store.Load(t.Context()); err == nil {
			t.Fatal("invalid file loaded")
		}
		if err := store.Save(t.Context(), Stored{}); err == nil {
			t.Fatal("invalid file overwritten")
		}
		data, _ := os.ReadFile(path)
		if string(data) != input {
			t.Fatal("original file modified")
		}
	}
	path := filepath.Join(t.TempDir(), "remote-access.json")
	store, _ := NewFileStore(path, fixtureProtector{})
	store.Save(t.Context(), Stored{Enabled: true, Token: bytes.Repeat([]byte{2}, 32)})
	original, _ := os.ReadFile(path)
	store.protector = fixtureProtector{fail: true}
	if _, err := store.Load(t.Context()); err == nil {
		t.Fatal("wrong protector accepted")
	}
	if store.Save(t.Context(), Stored{}) == nil {
		t.Fatal("wrong-user file overwritten")
	}
	data, _ := os.ReadFile(path)
	if !bytes.Equal(data, original) {
		t.Fatal("ciphertext lost")
	}
}
func TestRemoteFileBoundsPathsAndCancellation(t *testing.T) {
	path := filepath.Join(t.TempDir(), "remote-access.json")
	store, _ := NewFileStore(path, fixtureProtector{})
	if store.Save(t.Context(), Stored{Enabled: true}) == nil || store.Save(t.Context(), Stored{Token: []byte{1}}) == nil {
		t.Fatal("invalid token persisted")
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := store.Load(ctx); err == nil {
		t.Fatal("cancellation ignored")
	}
	if store.Save(ctx, Stored{}) == nil {
		t.Fatal("cancelled write")
	}
	os.WriteFile(path, bytes.Repeat([]byte{' '}, 16385), 0600)
	if _, err := store.Load(t.Context()); err == nil {
		t.Fatal("unbounded file")
	}
	for _, p := range []string{"relative.json", `\\server\share\remote-access.json`, filepath.Join(t.TempDir(), "bad.txt")} {
		if _, err := NewFileStore(p, fixtureProtector{}); err == nil {
			t.Fatal("unsafe path")
		}
	}
	if _, err := NewFileStore(path, nil); err == nil {
		t.Fatal("nil protector")
	}
}
