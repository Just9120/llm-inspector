//go:build windows

package resources

import (
	"encoding/binary"
	"net"
	"net/url"
	"strconv"
	"strings"
	"unsafe"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/winhost"
	"golang.org/x/sys/windows"
)

var tcpTable = windows.NewLazySystemDLL("iphlpapi.dll").NewProc("GetExtendedTcpTable")

type WindowsResolver struct{}

func (WindowsResolver) Resolve(endpoint string) *domain.ProcessAssociation {
	u, err := url.Parse(endpoint)
	if err != nil || u.User != nil || (u.Scheme != "http" && u.Scheme != "https") {
		return nil
	}
	host := u.Hostname()
	if strings.EqualFold(host, "localhost") {
		host = "127.0.0.1"
	}
	ip := net.ParseIP(host)
	if ip == nil || !ip.IsLoopback() {
		return nil
	}
	port, err := strconv.Atoi(u.Port())
	if err != nil {
		if u.Port() != "" {
			return nil
		}
		port = 80
		if u.Scheme == "https" {
			port = 443
		}
	}
	if port < 1 || port > 65535 {
		return nil
	}
	pid := listenerOwner(ip, port)
	if pid == 0 {
		return nil
	}
	h, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
	if err != nil {
		return nil
	}
	defer windows.CloseHandle(h)
	identity, err := winhost.IdentityForHandle(h, pid)
	// Keep the handle alive across revalidation: an exited process cannot have
	// its PID reused under us. A changed/disappeared listener is unavailable.
	if err != nil || listenerOwner(ip, port) != pid {
		return nil
	}
	return identity.Association()
}

func listenerOwner(ip net.IP, port int) uint32 {
	family := uintptr(2)
	if ip.To4() == nil {
		family = 23
	}
	var size uint32
	code, _, _ := tcpTable.Call(0, uintptr(unsafe.Pointer(&size)), 0, family, 3, 0)
	if code != 122 || size < 4 || size > 4<<20 {
		return 0
	}
	// The listener table can grow between sizing and capture. Retry boundedly.
	for attempts := 0; attempts < 3; attempts++ {
		if size < 4 || size > 4<<20 {
			return 0
		}
		buf := make([]byte, size)
		code, _, _ = tcpTable.Call(uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)), 0, family, 3, 0)
		if code == 122 {
			continue
		}
		if code != 0 || int(size) > len(buf) {
			return 0
		}
		return ownerFromTable(buf[:size], ip, port)
	}
	return 0
}

func ownerFromTable(data []byte, ip net.IP, port int) uint32 {
	if len(data) < 4 {
		return 0
	}
	count := uint64(binary.LittleEndian.Uint32(data))
	rowSize, portOffset, pidOffset, addrOffset, addrSize, stateOffset := 24, 8, 20, 4, 4, 0
	if ip.To4() == nil {
		rowSize, portOffset, pidOffset, addrOffset, addrSize, stateOffset = 56, 20, 52, 0, 16, 48
	}
	if count > uint64((len(data)-4)/rowSize) {
		return 0
	}
	var owner uint32
	for i := 0; i < int(count); i++ {
		row := data[4+i*rowSize : 4+(i+1)*rowSize]
		if binary.LittleEndian.Uint32(row[stateOffset:]) != 2 || int(binary.BigEndian.Uint16(row[portOffset:])) != port {
			continue
		}
		address := net.IP(row[addrOffset : addrOffset+addrSize])
		if !address.IsUnspecified() && !address.Equal(ip) {
			continue
		}
		pid := binary.LittleEndian.Uint32(row[pidOffset:])
		if pid == 0 {
			return 0
		}
		if owner != 0 && owner != pid {
			return 0
		}
		owner = pid
	}
	return owner
}
