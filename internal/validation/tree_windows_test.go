//go:build windows

package validation

import (
	"bufio"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"os/exec"
	"strings"
	"syscall"
	"testing"
	"time"
)

func TestCounterChildHelper(t *testing.T) {
	if os.Getenv("LLM_INSPECTOR_COUNTER_TEST_CHILD") != "1" {
		return
	}
	fmt.Println("ready")
	_, _ = io.Copy(io.Discard, os.Stdin)
}

func selfIdentity(t *testing.T) (string, string) {
	t.Helper()
	path, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	f, err := os.Open(path)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	h := sha256.New()
	if _, err = io.Copy(h, f); err != nil {
		t.Fatal(err)
	}
	return path, hex.EncodeToString(h.Sum(nil))
}

func TestWindowsCaptureOnlyReadsExactRootAndRetainsDescendants(t *testing.T) {
	path, hash := selfIdentity(t)
	tree, err := AttachProcessTree(uint32(os.Getpid()), path, hash)
	if err != nil {
		t.Fatal(err)
	}
	defer tree.Close()
	before, err := tree.Read()
	if err != nil || before.Live < 1 || before.PrivateBytes == 0 {
		t.Fatal(before, err)
	}
	child := exec.CommandContext(t.Context(), path, "-test.run=^TestCounterChildHelper$")
	child.Env = append(os.Environ(), "LLM_INSPECTOR_COUNTER_TEST_CHILD=1")
	child.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	in, err := child.StdinPipe()
	if err != nil {
		t.Fatal(err)
	}
	out, err := child.StdoutPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err = child.Start(); err != nil {
		t.Fatal(err)
	}
	waited := false
	t.Cleanup(func() {
		in.Close()
		if !waited {
			_ = child.Wait()
		}
	})
	line, err := bufio.NewReader(out).ReadString('\n')
	if err != nil || strings.TrimSpace(line) != "ready" {
		t.Fatal("child readiness", err)
	}
	during, err := tree.Read()
	if err != nil || during.Retained <= before.Retained {
		t.Fatal("missing descendant", during, err)
	}
	in.Close()
	err = child.Wait()
	waited = true
	if err != nil {
		t.Fatal(err)
	}
	after, err := tree.Read()
	if err != nil || after.Retained != during.Retained || after.Live >= during.Live || after.CPU100ns < during.CPU100ns {
		t.Fatal("lost terminal descendant counters", after, err)
	}
	tree.Close()
	tree.Close()
	if _, err = tree.Read(); err == nil {
		t.Fatal("closed tree accepted")
	}
}

func TestWindowsCaptureRejectsWrongIdentityAndParentAge(t *testing.T) {
	path, hash := selfIdentity(t)
	for _, c := range []struct {
		pid        uint32
		path, hash string
	}{
		{0, path, hash}, {uint32(os.Getpid()), "relative.exe", hash},
		{uint32(os.Getpid()), path, "not-a-hash"}, {uint32(os.Getpid()), path, strings.Repeat("0", 64)},
		{uint32(os.Getpid()), os.DevNull, hash},
	} {
		if tree, err := AttachProcessTree(c.pid, c.path, c.hash); err == nil {
			tree.Close()
			t.Fatal("wrong identity accepted")
		}
	}
	now := time.Now()
	if validChild(now, now.Add(-time.Second)) || validChild(time.Time{}, now) || validChild(now, time.Time{}) || !validChild(now, now.Add(time.Second)) {
		t.Fatal("PID reuse/parent-age guard")
	}
	if validSnapshotChild(now, now.Add(time.Second), now) || validSnapshotChild(now, now, time.Time{}) || !validSnapshotChild(now, now.Add(time.Second), now.Add(2*time.Second)) {
		t.Fatal("PID reused after snapshot accepted")
	}
}
