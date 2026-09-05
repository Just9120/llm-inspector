//go:build windows

package remote

import (
	"golang.org/x/sys/windows"
	"runtime"
	"unsafe"
)

type WindowsProtector struct{}

func (WindowsProtector) Protect(input []byte) ([]byte, error)   { return transform(input, true) }
func (WindowsProtector) Unprotect(input []byte) ([]byte, error) { return transform(input, false) }
func transform(input []byte, protect bool) ([]byte, error) {
	if len(input) == 0 || len(input) > 4096 {
		return nil, ErrConfiguration
	}
	copyInput := append([]byte(nil), input...)
	defer clear(copyInput)
	in := windows.DataBlob{Size: uint32(len(copyInput)), Data: &copyInput[0]}
	var out windows.DataBlob
	// CRYPTPROTECT_UI_FORBIDDEN only: no LOCAL_MACHINE, entropy or prompts,
	// preserving the legacy .NET CurrentUser ciphertext contract.
	var err error
	if protect {
		err = windows.CryptProtectData(&in, nil, nil, 0, nil, 1, &out)
	} else {
		err = windows.CryptUnprotectData(&in, nil, nil, 0, nil, 1, &out)
	}
	runtime.KeepAlive(copyInput)
	if out.Data != nil {
		defer windows.LocalFree(windows.Handle(unsafe.Pointer(out.Data)))
		if out.Size > 0 && out.Size <= 65536 {
			defer clear(unsafe.Slice(out.Data, int(out.Size)))
		}
	}
	if err != nil || out.Data == nil || out.Size == 0 || out.Size > 4096 {
		return nil, ErrConfiguration
	}
	return append([]byte(nil), unsafe.Slice(out.Data, int(out.Size))...), nil
}
