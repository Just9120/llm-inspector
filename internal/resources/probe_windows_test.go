//go:build windows

package resources

import (
	"encoding/binary"
	"net"
	"os"
	"testing"
	"time"
	"unsafe"

	"github.com/Just9120/llm-inspector/internal/winhost"
)

func TestWindowsNativeProbeAndExactListenerOwner(t *testing.T) {
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	association := (WindowsResolver{}).Resolve("http://" + listener.Addr().String())
	if association == nil || association.PID != os.Getpid() {
		t.Fatal("self listener ownership unavailable")
	}
	p := WindowsProbe{} // No external GPU process in deterministic native test.
	s, err := p.Capture(t.Context(), association)
	if err != nil {
		t.Fatal(err)
	}
	if !s.MemoryAvailable || s.TotalMemory == 0 || s.Process == nil || s.Process.WorkingSet == 0 {
		t.Fatal("native resource sources unavailable")
	}
	stale := *association
	stale.StartedAt = stale.StartedAt.Add(time.Second)
	if captureProcess(&stale) != nil {
		t.Fatal("reused PID timestamp accepted")
	}
	stale = *association
	stale.ImageName = "different.exe"
	if captureProcess(&stale) != nil {
		t.Fatal("mismatched image accepted")
	}
	if (WindowsResolver{}).Resolve("https://backend.tailnet.example.ts.net") != nil {
		t.Fatal("remote process attributed locally")
	}
	if winhost.OSVersion() == "" {
		t.Fatal("OS version unavailable")
	}
	if unsafe.Sizeof(memoryStatusEx{}) != 64 || unsafe.Sizeof(processMemoryCounters{}) != 80 || unsafe.Sizeof(ioCounters{}) != 48 {
		t.Fatal("Windows x64 ABI layout drift")
	}
}

func TestTCPTableBoundsAddressAndAmbiguousOwners(t *testing.T) {
	port := uint16(11434)
	row := func(pid uint32, ip string) []byte {
		b := make([]byte, 24)
		binary.LittleEndian.PutUint32(b, 2)
		copy(b[4:8], net.ParseIP(ip).To4())
		binary.BigEndian.PutUint16(b[8:], port)
		binary.LittleEndian.PutUint32(b[20:], pid)
		return b
	}
	table := func(rows ...[]byte) []byte {
		b := make([]byte, 4)
		binary.LittleEndian.PutUint32(b, uint32(len(rows)))
		for _, r := range rows {
			b = append(b, r...)
		}
		return b
	}
	loop := net.ParseIP("127.0.0.1")
	if ownerFromTable(table(row(42, "127.0.0.1"), row(43, "192.168.0.2")), loop, int(port)) != 42 {
		t.Fatal("unrelated interface collided")
	}
	if ownerFromTable(table(row(42, "0.0.0.0"), row(43, "127.0.0.1")), loop, int(port)) != 0 {
		t.Fatal("ambiguous owners accepted")
	}
	if ownerFromTable([]byte{255, 255, 255, 255}, loop, int(port)) != 0 {
		t.Fatal("unbounded row count")
	}
	v6 := make([]byte, 56)
	copy(v6[:16], net.ParseIP("::1").To16())
	binary.BigEndian.PutUint16(v6[20:], port)
	binary.LittleEndian.PutUint32(v6[48:], 2)
	binary.LittleEndian.PutUint32(v6[52:], 42)
	if ownerFromTable(table(v6), net.ParseIP("::1"), int(port)) != 42 {
		t.Fatal("IPv6 owner mapping")
	}
}

func TestDriverOutputBufferBound(t *testing.T) {
	b := boundedOutput{}
	if _, err := b.Write(make([]byte, 16*1024)); err != nil {
		t.Fatal(err)
	}
	if _, err := b.Write([]byte{1}); err == nil || !b.overflow || len(b.bytes) != 16*1024 {
		t.Fatal("driver output unbounded")
	}
}
