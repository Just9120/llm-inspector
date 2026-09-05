//go:build windows

package remote

import (
	"bytes"
	"context"
	"encoding/base64"
	"golang.org/x/sys/windows"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"testing"
	"time"
)

func TestWindowsDPAPIRoundTripAndTampering(t *testing.T) {
	p := WindowsProtector{}
	token := bytes.Repeat([]byte{19}, 32)
	cipher, err := p.Protect(token)
	if err != nil {
		t.Fatal("DPAPI protect failed")
	}
	defer clear(cipher)
	if bytes.Equal(cipher, token) {
		t.Fatal("plaintext returned")
	}
	plain, err := p.Unprotect(cipher)
	if err != nil || !bytes.Equal(plain, token) {
		t.Fatal("DPAPI roundtrip")
	}
	clear(plain)
	cipher[len(cipher)-1] ^= 1
	if _, err = p.Unprotect(cipher); err == nil {
		t.Fatal("tampering accepted")
	}
	if _, err = p.Protect(nil); err == nil {
		t.Fatal("empty input accepted")
	}
	if _, err = p.Unprotect(make([]byte, 4097)); err == nil {
		t.Fatal("unbounded input accepted")
	}
	store, _ := NewFileStore(filepath.Join(t.TempDir(), "remote-access.json"), p)
	if err = store.Save(t.Context(), Stored{true, token, nil}); err != nil {
		t.Fatal(err)
	}
	loaded, err := store.Load(t.Context())
	if err != nil || !bytes.Equal(loaded.Token, token) {
		t.Fatal("protected store", err)
	}
	clear(loaded.Token)
}
func TestDPAPILegacyDotnetCurrentUserInteroperability(t *testing.T) {
	system, err := windows.GetSystemDirectory()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(t.Context(), 10*time.Second)
	defer cancel()
	// Fixed synthetic bytes only; no existing user credential is read and no
	// plaintext/token is written to tool output. .NET uses the legacy API model.
	script := `Add-Type -AssemblyName System.Security; [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect(([byte[]](1..32)), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))`
	cmd := exec.CommandContext(ctx, filepath.Join(system, "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoProfile", "-NonInteractive", "-Command", script)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	cmd.WaitDelay = time.Second
	data, err := cmd.Output()
	if err != nil {
		t.Fatal("legacy DPAPI fixture generation failed")
	}
	cipher, err := base64.StdEncoding.DecodeString(strings.TrimSpace(string(data)))
	if err != nil {
		t.Fatal("legacy ciphertext encoding")
	}
	defer clear(cipher)
	plain, err := (WindowsProtector{}).Unprotect(cipher)
	if err != nil {
		t.Fatal("legacy DPAPI compatibility failed")
	}
	defer clear(plain)
	if len(plain) != 32 {
		t.Fatal("legacy length")
	}
	for i, b := range plain {
		if b != byte(i+1) {
			t.Fatal("legacy token changed")
		}
	}
}
