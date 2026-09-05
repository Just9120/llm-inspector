//go:build windows

package resources

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/winhost"
	"golang.org/x/sys/windows"
)

var kernel = windows.NewLazySystemDLL("kernel32.dll")
var systemTimes = kernel.NewProc("GetSystemTimes")
var memoryStatus = kernel.NewProc("GlobalMemoryStatusEx")
var processIO = kernel.NewProc("GetProcessIoCounters")
var processMemory = windows.NewLazySystemDLL("psapi.dll").NewProc("GetProcessMemoryInfo")

type memoryStatusEx struct {
	Length, Load                                                                                                                 uint32
	TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual uint64
}
type ioCounters struct{ ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes uint64 }
type processMemoryCounters struct {
	Size, PageFaults                                                                                                           uint32
	PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, PrivateUsage uintptr
}

type WindowsProbe struct{ gpuExecutable string }

func NewWindowsProbe() *WindowsProbe { return &WindowsProbe{gpuExecutable: findNvidiaSMI()} }
func (p *WindowsProbe) Capture(ctx context.Context, association *domain.ProcessAssociation) (Snapshot, error) {
	if err := ctx.Err(); err != nil {
		return Snapshot{}, err
	}
	s := Snapshot{CapturedAt: time.Now(), GPUs: []GPU{}}
	var idle, kern, user windows.Filetime
	ok, _, _ := systemTimes.Call(uintptr(unsafe.Pointer(&idle)), uintptr(unsafe.Pointer(&kern)), uintptr(unsafe.Pointer(&user)))
	// GetSystemTimes covers one primary group above 64 CPUs. Do not label that
	// partial value as total-host utilization on such machines.
	s.CPUAvailable = ok != 0 && windows.GetActiveProcessorCount(0xffff) <= 64
	if s.CPUAvailable {
		s.Idle = ticks(idle)
		s.Kernel = ticks(kern)
		s.User = ticks(user)
	}
	m := memoryStatusEx{}
	m.Length = uint32(unsafe.Sizeof(m))
	ok, _, _ = memoryStatus.Call(uintptr(unsafe.Pointer(&m)))
	s.MemoryAvailable = ok != 0
	if s.MemoryAvailable {
		s.TotalMemory = m.TotalPhysical
		s.AvailableMemory = m.AvailablePhysical
	}
	if association.Valid() {
		s.Process = captureProcess(association)
	}
	s.GPUs = captureNvidia(ctx, p.gpuExecutable)
	return s, nil
}
func ticks(t windows.Filetime) uint64 { return uint64(t.HighDateTime)<<32 | uint64(t.LowDateTime) }
func captureProcess(association *domain.ProcessAssociation) *ProcessSnapshot {
	h, err := windows.OpenProcess(windows.PROCESS_QUERY_INFORMATION|windows.PROCESS_VM_READ, false, uint32(association.PID))
	if err != nil {
		return nil
	}
	defer windows.CloseHandle(h)
	identity, err := winhost.IdentityForHandle(h, uint32(association.PID))
	if err != nil || !identity.StartedAt.Equal(association.StartedAt) || !strings.EqualFold(filepath.Base(identity.ImagePath), association.ImageName) {
		return nil
	}
	var creation, exit, kernel, user windows.Filetime
	if err = windows.GetProcessTimes(h, &creation, &exit, &kernel, &user); err != nil {
		return nil
	}
	var counters ioCounters
	ok, _, _ := processIO.Call(uintptr(h), uintptr(unsafe.Pointer(&counters)))
	if ok == 0 {
		return nil
	}
	m := processMemoryCounters{}
	m.Size = uint32(unsafe.Sizeof(m))
	ok, _, _ = processMemory.Call(uintptr(h), uintptr(unsafe.Pointer(&m)), uintptr(m.Size))
	if ok == 0 {
		return nil
	}
	return &ProcessSnapshot{CPU100ns: ticks(kernel) + ticks(user), WorkingSet: uint64(m.WorkingSet), ReadBytes: counters.ReadBytes, WriteBytes: counters.WriteBytes}
}

func findNvidiaSMI() string {
	var candidates []string
	if system, err := windows.GetSystemDirectory(); err == nil {
		candidates = append(candidates, filepath.Join(system, "nvidia-smi.exe"))
	}
	if programFiles, err := windows.KnownFolderPath(windows.FOLDERID_ProgramFiles, 0); err == nil {
		candidates = append(candidates, filepath.Join(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"))
	}
	for _, path := range candidates {
		if info, err := os.Stat(path); err == nil && !info.IsDir() {
			return path
		}
	}
	return ""
}

type boundedOutput struct {
	bytes    []byte
	overflow bool
}

func (b *boundedOutput) Write(p []byte) (int, error) {
	if len(b.bytes)+len(p) > 16*1024 {
		b.overflow = true
		return 0, errors.New("driver output exceeds bounded telemetry contract")
	}
	b.bytes = append(b.bytes, p...)
	return len(p), nil
}
func captureNvidia(ctx context.Context, path string) []GPU {
	if path == "" {
		return []GPU{}
	}
	ctx, cancel := context.WithTimeout(ctx, time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, path, "--query-gpu=index,uuid,driver_version,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw", "--format=csv,noheader,nounits")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	cmd.WaitDelay = 100 * time.Millisecond
	var output boundedOutput
	cmd.Stdout = &output
	cmd.Stderr = io.Discard
	if err := cmd.Run(); err != nil || output.overflow {
		return []GPU{}
	}
	return ParseNvidiaCSV(string(output.bytes))
}
