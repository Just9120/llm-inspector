package artifact

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"os"
	"path/filepath"
	"strings"
)

// Save writes exactly the bytes already previewed, not a regenerated snapshot.
// Only a local .json destination is accepted; this package has no network client.
func Save(ctx context.Context, a Artifact, path string) error {
	h := sha256.Sum256(a.data)
	if len(a.data) == 0 || a.JSON != string(a.data) || a.SHA256 != hex.EncodeToString(h[:]) {
		return ErrArtifact
	}
	if strings.TrimSpace(path) == "" || strings.HasPrefix(path, `\\`) || strings.HasPrefix(path, "//") || strings.Contains(path, "://") {
		return ErrArtifact
	}
	abs, err := filepath.Abs(path)
	if err != nil {
		return err
	}
	if !strings.EqualFold(filepath.Ext(abs), ".json") {
		return ErrArtifact
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	dir := filepath.Dir(abs)
	if err = os.MkdirAll(dir, 0700); err != nil {
		return err
	}
	f, err := os.CreateTemp(dir, ".inspector-preview-*.tmp")
	if err != nil {
		return err
	}
	temp := f.Name()
	defer os.Remove(temp)
	if _, err = f.Write(a.data); err != nil {
		f.Close()
		return err
	}
	if err = f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err = f.Close(); err != nil {
		return err
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	return os.Rename(temp, abs)
}
