//go:build windows

// Package winhost owns narrow Windows API calls. Process paths are transient
// ownership evidence, never telemetry/history/export fields.
package winhost

import (
	"fmt"
	"path/filepath"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"golang.org/x/sys/windows"
)

type Identity struct {
	PID       uint32
	StartedAt time.Time
	ImagePath string
}

func IdentityForHandle(handle windows.Handle, pid uint32) (Identity, error) {
	var creation, exit, kernel, user windows.Filetime
	if err := windows.GetProcessTimes(handle, &creation, &exit, &kernel, &user); err != nil {
		return Identity{}, err
	}
	buf := make([]uint16, 32768)
	size := uint32(len(buf))
	if err := windows.QueryFullProcessImageName(handle, 0, &buf[0], &size); err != nil {
		return Identity{}, err
	}
	return Identity{PID: pid, StartedAt: time.Unix(0, creation.Nanoseconds()).UTC(), ImagePath: windows.UTF16ToString(buf[:size])}, nil
}
func ProcessIdentity(pid uint32) (Identity, error) {
	h, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
	if err != nil {
		return Identity{}, err
	}
	defer windows.CloseHandle(h)
	return IdentityForHandle(h, pid)
}
func (p Identity) Association() *domain.ProcessAssociation {
	name := domain.TechnicalIdentifier(filepath.Base(p.ImagePath))
	if p.PID == 0 || p.StartedAt.IsZero() || name == "" {
		return nil
	}
	return &domain.ProcessAssociation{PID: int(p.PID), StartedAt: p.StartedAt, ImageName: name, SourceVersion: "windows-ip-helper-listener-owner-v1"}
}
func OSVersion() string {
	v := windows.RtlGetVersion()
	return fmt.Sprintf("%d.%d.%d", v.MajorVersion, v.MinorVersion, v.BuildNumber)
}
func LocalDataPath() (string, error) {
	return windows.KnownFolderPath(windows.FOLDERID_LocalAppData, 0)
}
