//go:build windows

package background

import (
	"context"
	"golang.org/x/sys/windows"
	"strings"
	"sync"
	"testing"
	"time"
	"unicode/utf16"
	"unsafe"
)

func TestNativeTrayABILayoutAndRussianUTF16(t *testing.T) {
	n := notifyIconData{}
	if unsafe.Sizeof(n) != 976 || unsafe.Offsetof(n.Window) != 8 || unsafe.Offsetof(n.Info) != 304 || unsafe.Offsetof(n.InfoTitle) != 820 || unsafe.Offsetof(n.GUID) != 952 || unsafe.Offsetof(n.BalloonIcon) != 968 {
		t.Fatalf("NOTIFYICONDATAW size/offset drift: %d", unsafe.Sizeof(n))
	}
	if unsafe.Sizeof(windowClass{}) != 72 || unsafe.Sizeof(windowMessage{}) != 48 || unsafe.Sizeof(point{}) != 8 {
		t.Fatal("Win32 message ABI")
	}
	var buffer [6]uint16
	copyUTF16(buffer[:], "Я😀😀!")
	if windows.UTF16ToString(buffer[:]) != "Я😀😀" || buffer[5] != 0 {
		t.Fatal("surrogate truncation", buffer)
	}
	copyUTF16(buffer[:], "a\x00b\nc")
	if windows.UTF16ToString(buffer[:]) != "a b c" {
		t.Fatal("embedded controls")
	}
	for _, item := range trayMenu(false) {
		if item.Command != 0 && len(item.Text) == 0 {
			t.Fatal("menu text")
		}
	}
	if !strings.HasPrefix(trayMenu(true)[2].Text, "Возобновить") {
		t.Fatal("paused menu")
	}
}
func TestNativeHiddenTrayMessageLoopWithoutUserNotifications(t *testing.T) {
	var mu sync.Mutex
	var records []struct {
		action uintptr
		data   notifyIconData
	}
	opened := make(chan bool, 1)
	fakeNotify := func(action uintptr, n *notifyIconData) bool {
		mu.Lock()
		defer mu.Unlock()
		records = append(records, struct {
			action uintptr
			data   notifyIconData
		}{action, *n})
		return true
	}
	tray, err := newWindowsTray(TrayCommands{Show: func(settings bool) { opened <- settings }}, nil, fakeNotify)
	if err != nil {
		t.Fatal(err)
	}
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		if err := tray.Close(ctx); err != nil {
			t.Error(err)
		}
	}()
	if !tray.Available() || tray.window.Load() == 0 {
		t.Fatal("native hidden window unavailable")
	}
	if err := tray.Publish(Notification{Event: LongOperationCompleted, Title: "Тест", Body: "Проверка тихого режима", Silent: true}); err != nil {
		t.Fatal(err)
	}
	postMessage.Call(tray.window.Load(), wmTray, 0, (1<<16)|0x400)
	select {
	case settings := <-opened:
		if settings {
			t.Fatal("wrong command")
		}
	case <-time.After(time.Second):
		t.Fatal("callback lost")
	}
	// Simulate the message only for our own hidden HWND, not a global Explorer
	// restart/broadcast. Shell_NotifyIcon is replaced, so no user icon or balloon.
	postMessage.Call(tray.window.Load(), uintptr(tray.taskbarCreated), 0, 0)
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := tray.Close(ctx); err != nil {
		t.Fatal(err)
	}
	if tray.Available() || tray.Publish(Notification{}) == nil {
		t.Fatal("closed tray accepted notification")
	}
	mu.Lock()
	defer mu.Unlock()
	adds, deletes, balloons := 0, 0, 0
	for _, r := range records {
		switch r.action {
		case nimAdd:
			adds++
		case nimDelete:
			deletes++
		case nimModify:
			if r.data.Flags == 0x10 {
				balloons++
				if r.data.InfoFlags&0x10 == 0 || windows.UTF16ToString(r.data.InfoTitle[:]) != "Тест" {
					t.Fatal("native notification flags/text")
				}
				_ = utf16.Decode(r.data.Info[:])
			}
		}
	}
	if adds < 2 || deletes < 1 || balloons != 1 || tray.Failures() != 0 {
		t.Fatal("message lifecycle", adds, deletes, balloons, tray.Failures())
	}
}
func TestNativeTrayStartupFailureCleansOwnedWindow(t *testing.T) {
	before := 0
	trayWindows.Range(func(any, any) bool { before++; return true })
	if _, err := newWindowsTray(TrayCommands{}, nil, func(uintptr, *notifyIconData) bool { return false }); err == nil {
		t.Fatal("native failure accepted")
	}
	// Startup signals the error before deferred cleanup; wait for the owned
	// callback-map entry to disappear without mutating any external window.
	deadline := time.Now().Add(time.Second)
	for time.Now().Before(deadline) {
		count := 0
		trayWindows.Range(func(any, any) bool { count++; return true })
		if count == before {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("failed tray leaked owned window")
}
