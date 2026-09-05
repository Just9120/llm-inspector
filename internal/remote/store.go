package remote

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

type Protector interface {
	Protect([]byte) ([]byte, error)
	Unprotect([]byte) ([]byte, error)
}
type FileStore struct {
	mu        sync.Mutex
	path      string
	protector Protector
}
type credentialFile struct {
	SchemaVersion        int        `json:"schema_version"`
	Enabled              bool       `json:"enabled"`
	ProtectedBearerToken *string    `json:"protected_bearer_token"`
	UpdatedAt            *time.Time `json:"updated_at"`
}

func NewFileStore(path string, p Protector) (*FileStore, error) {
	if !filepath.IsAbs(path) || strings.HasPrefix(path, `\\`) || !strings.EqualFold(filepath.Ext(path), ".json") || p == nil {
		return nil, ErrConfiguration
	}
	return &FileStore{path: filepath.Clean(path), protector: p}, nil
}
func (s *FileStore) Load(ctx context.Context) (Stored, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.load(ctx)
}
func (s *FileStore) load(ctx context.Context) (Stored, error) {
	if err := ctx.Err(); err != nil {
		return Stored{}, err
	}
	f, err := os.Open(s.path)
	if errors.Is(err, os.ErrNotExist) {
		return Stored{}, nil
	}
	if err != nil {
		return Stored{}, ErrConfiguration
	}
	defer f.Close()
	data, err := io.ReadAll(io.LimitReader(f, 16385))
	if err != nil || len(data) > 16384 {
		return Stored{}, ErrConfiguration
	}
	var file credentialFile
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if d.Decode(&file) != nil || d.Decode(new(any)) != io.EOF || file.SchemaVersion != 1 {
		return Stored{}, ErrConfiguration
	}
	result := Stored{Enabled: file.Enabled, UpdatedAt: file.UpdatedAt}
	if file.ProtectedBearerToken != nil {
		if len(*file.ProtectedBearerToken) > 4096 {
			return Stored{}, ErrConfiguration
		}
		cipher, err := base64.StdEncoding.Strict().DecodeString(*file.ProtectedBearerToken)
		if err != nil || len(cipher) == 0 {
			return Stored{}, ErrConfiguration
		}
		defer clear(cipher)
		result.Token, err = s.protector.Unprotect(cipher)
		if err != nil {
			clear(result.Token)
			return Stored{}, ErrConfiguration
		}
	}
	if err = result.validate(); err != nil {
		clear(result.Token)
		return Stored{}, err
	}
	return result, nil
}
func (s *FileStore) Save(ctx context.Context, value Stored) error {
	if err := value.validate(); err != nil {
		return err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	// Current-user/schema integrity failure is not a license to replace a file.
	existing, err := s.load(ctx)
	clear(existing.Token)
	if err != nil {
		return err
	}
	var encoded *string
	if value.Token != nil {
		cipher, err := s.protector.Protect(value.Token)
		if err != nil {
			return ErrConfiguration
		}
		defer clear(cipher)
		v := base64.StdEncoding.EncodeToString(cipher)
		if len(v) == 0 || len(v) > 4096 {
			return ErrConfiguration
		}
		encoded = &v
	}
	at := value.UpdatedAt
	if at == nil {
		now := time.Now().UTC()
		at = &now
	}
	data, err := json.MarshalIndent(credentialFile{1, value.Enabled, encoded, at}, "", "  ")
	if err != nil {
		return ErrConfiguration
	}
	if err = os.MkdirAll(filepath.Dir(s.path), 0700); err != nil {
		return ErrConfiguration
	}
	f, err := os.CreateTemp(filepath.Dir(s.path), ".remote-access-*.tmp")
	if err != nil {
		return ErrConfiguration
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if _, err = f.Write(data); err == nil {
		err = f.Sync()
	}
	closeErr := f.Close()
	if err != nil || closeErr != nil {
		return ErrConfiguration
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	if os.Rename(tmp, s.path) != nil {
		return ErrConfiguration
	}
	return nil
}
