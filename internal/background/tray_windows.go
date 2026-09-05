//go:build windows

package background

import (
	"context"
	"errors"
	"fmt"
	"os"
	"runtime"
	"sync"
	"sync/atomic"
	"time"
	"unicode/utf16"
	"unsafe"

	"golang.org/x/sys/windows"
)

var trayUser32 = windows.NewLazySystemDLL("user32.dll")
var trayShell32 = windows.NewLazySystemDLL("shell32.dll")
var registerClass = trayUser32.NewProc("RegisterClassW")
var unregisterClass = trayUser32.NewProc("UnregisterClassW")
var createWindow = trayUser32.NewProc("CreateWindowExW")
var destroyWindow = trayUser32.NewProc("DestroyWindow")
var defWindowProc = trayUser32.NewProc("DefWindowProcW")
var getMessage = trayUser32.NewProc("GetMessageW")
var translateMessage = trayUser32.NewProc("TranslateMessage")
var dispatchMessage = trayUser32.NewProc("DispatchMessageW")
var postMessage = trayUser32.NewProc("PostMessageW")
var postQuitMessage = trayUser32.NewProc("PostQuitMessage")
var loadIcon = trayUser32.NewProc("LoadIconW")
var shellNotifyIcon = trayShell32.NewProc("Shell_NotifyIconW")
var registerWindowMessage = trayUser32.NewProc("RegisterWindowMessageW")
var createPopupMenu = trayUser32.NewProc("CreatePopupMenu")
var appendMenu = trayUser32.NewProc("AppendMenuW")
var destroyMenu = trayUser32.NewProc("DestroyMenu")
var setForegroundWindow = trayUser32.NewProc("SetForegroundWindow")
var getCursorPos = trayUser32.NewProc("GetCursorPos")
var trackPopupMenu = trayUser32.NewProc("TrackPopupMenu")

const (
	wmTray    = 0x8001
	wmPublish = 0x8002
	wmClose   = 0x10
	wmDestroy = 2
)
const (
	nimAdd        = 0
	nimModify     = 1
	nimDelete     = 2
	nimSetVersion = 4
)

type windowClass struct {
	Style                              uint32
	Procedure                          uintptr
	ClassExtra, WindowExtra            int32
	Instance, Icon, Cursor, Background uintptr
	MenuName, ClassName                *uint16
}
type point struct{ X, Y int32 }
type windowMessage struct {
	Window         uintptr
	Value          uint32
	WParam, LParam uintptr
	Time           uint32
	Point          point
	Private        uint32
}

// Native x64 layout: HWND/HICON are pointer-sized; GUID remains 16 bytes.
// https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
type notifyIconData struct {
	Size                uint32
	Window              uintptr
	ID, Flags, Callback uint32
	Icon                uintptr
	Tip                 [128]uint16
	State, StateMask    uint32
	Info                [256]uint16
	TimeoutOrVersion    uint32
	InfoTitle           [64]uint16
	InfoFlags           uint32
	GUID                [16]byte
	BalloonIcon         uintptr
}
type queuedNotification struct {
	notification Notification
	result       chan error
}
type nativeNotifier func(uintptr, *notifyIconData) bool

var trayWindows sync.Map
var traySequence atomic.Uint64
var trayCallback = windows.NewCallback(trayWindowProcedure)

type WindowsTray struct {
	window            atomic.Uintptr
	closed, available atomic.Bool
	failures          atomic.Uint64
	done              chan struct{}
	started           chan error
	notifications     chan queuedNotification
	commands          chan TrayCommand
	route             TrayCommands
	paused            func() bool
	notify            nativeNotifier
	// These fields are owned solely by the locked native message-loop thread.
	icon           uintptr
	taskbarCreated uint32
}

func NewWindowsTray(route TrayCommands, paused func() bool) (*WindowsTray, error) {
	return newWindowsTray(route, paused, func(action uintptr, data *notifyIconData) bool {
		r, _, _ := shellNotifyIcon.Call(action, uintptr(unsafe.Pointer(data)))
		return r != 0
	})
}
func newWindowsTray(route TrayCommands, paused func() bool, notify nativeNotifier) (*WindowsTray, error) {
	t := &WindowsTray{done: make(chan struct{}), started: make(chan error, 1), notifications: make(chan queuedNotification, 16), commands: make(chan TrayCommand, 16), route: route, paused: paused, notify: notify}
	go t.run()
	go t.runCommands()
	select {
	case err := <-t.started:
		if err != nil {
			<-t.done
			return nil, err
		}
		return t, nil
	case <-time.After(5 * time.Second):
		t.closed.Store(true)
		if w := t.window.Load(); w != 0 {
			postMessage.Call(w, wmClose, 0, 0)
		}
		return nil, errors.New("Windows tray не запустился за 5 секунд")
	}
}
func (t *WindowsTray) Available() bool  { return t.available.Load() && !t.closed.Load() }
func (t *WindowsTray) Failures() uint64 { return t.failures.Load() }
func (t *WindowsTray) Publish(n Notification) error {
	if !t.Available() {
		return errors.New("Windows tray недоступен")
	}
	q := queuedNotification{n, make(chan error, 1)}
	select {
	case t.notifications <- q:
	default:
		return errors.New("очередь Windows notifications заполнена")
	}
	r, _, _ := postMessage.Call(t.window.Load(), wmPublish, 0, 0)
	if r == 0 {
		return errors.New("не удалось передать уведомление Windows")
	}
	select {
	case err := <-q.result:
		return err
	case <-t.done:
		return errors.New("Windows tray остановлен")
	case <-time.After(time.Second):
		return errors.New("результат Windows notification не подтверждён")
	}
}
func (t *WindowsTray) Close(ctx context.Context) error {
	t.closed.Store(true)
	if w := t.window.Load(); w != 0 {
		postMessage.Call(w, wmClose, 0, 0)
	}
	select {
	case <-t.done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}
func (t *WindowsTray) run() {
	runtime.LockOSThread()
	// Let Go terminate this dedicated OS thread when the goroutine exits.
	// Unlocking would recycle native thread-local message state (including a
	// WM_QUIT posted during failed startup) into a later tray instance.
	defer close(t.done)
	defer t.available.Store(false)
	fail := func() { t.started <- errors.New("не удалось инициализировать Windows tray") }
	var instance windows.Handle
	if windows.GetModuleHandleEx(2, nil, &instance) != nil {
		fail()
		return
	}
	className, _ := windows.UTF16PtrFromString(fmt.Sprintf("LlmInspector.GoTray.%d.%d", os.Getpid(), traySequence.Add(1)))
	wc := windowClass{Procedure: trayCallback, Instance: uintptr(instance), ClassName: className}
	atom, _, _ := registerClass.Call(uintptr(unsafe.Pointer(&wc)))
	if atom == 0 {
		fail()
		return
	}
	defer unregisterClass.Call(uintptr(unsafe.Pointer(className)), uintptr(instance))
	title, _ := windows.UTF16PtrFromString("LLM Inspector tray")
	// Hidden top-level tool window receives TaskbarCreated broadcasts. A
	// message-only HWND would not receive them after Explorer restarts.
	window, _, _ := createWindow.Call(0x80, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(title)), 0x80000000, 0, 0, 0, 0, 0, 0, uintptr(instance), 0)
	if window == 0 {
		fail()
		return
	}
	t.window.Store(window)
	trayWindows.Store(window, t)
	defer func() { destroyWindow.Call(window); trayWindows.Delete(window); t.window.Store(0) }()
	defer func() { d := t.iconData(window); t.notify(nimDelete, &d) }()
	t.icon, _, _ = loadIcon.Call(0, 32512)
	if t.icon == 0 {
		fail()
		return
	}
	restart, _ := windows.UTF16PtrFromString("TaskbarCreated")
	msg, _, _ := registerWindowMessage.Call(uintptr(unsafe.Pointer(restart)))
	t.taskbarCreated = uint32(msg)
	if t.closed.Load() || !t.addIcon(window) {
		fail()
		return
	}
	t.available.Store(true)
	t.started <- nil
	var message windowMessage
	for {
		r, _, _ := getMessage.Call(uintptr(unsafe.Pointer(&message)), 0, 0, 0)
		if int32(r) <= 0 {
			if int32(r) < 0 {
				t.failures.Add(1)
			}
			return
		}
		translateMessage.Call(uintptr(unsafe.Pointer(&message)))
		dispatchMessage.Call(uintptr(unsafe.Pointer(&message)))
	}
}
func (t *WindowsTray) iconData(window uintptr) notifyIconData {
	return notifyIconData{Size: uint32(unsafe.Sizeof(notifyIconData{})), Window: window, ID: 1}
}
func (t *WindowsTray) addIcon(window uintptr) bool {
	d := t.iconData(window)
	d.Flags = 1 | 2 | 4 | 0x80
	d.Callback = wmTray
	d.Icon = t.icon
	copyUTF16(d.Tip[:], "LLM Inspector — мониторинг")
	if !t.notify(nimAdd, &d) && !t.notify(nimModify, &d) {
		return false
	}
	d.TimeoutOrVersion = 4
	if !t.notify(nimSetVersion, &d) {
		return false
	}
	return true
}
func trayWindowProcedure(window, message, wparam, lparam uintptr) (result uintptr) {
	v, ok := trayWindows.Load(window)
	if !ok {
		r, _, _ := defWindowProc.Call(window, message, wparam, lparam)
		return r
	}
	t := v.(*WindowsTray)
	defer func() {
		if recover() != nil {
			t.failures.Add(1)
			result = 0
		}
	}()
	if t.taskbarCreated != 0 && uint32(message) == t.taskbarCreated {
		ok := t.addIcon(window)
		t.available.Store(ok)
		if !ok {
			t.failures.Add(1)
		}
		return 0
	}
	switch message {
	case wmTray:
		if (lparam>>16)&0xffff != 1 {
			return 0
		}
		switch lparam & 0xffff {
		case 0x0202, 0x0400, 0x0401:
			t.queueCommand(OpenApplication)
		case 0x0205, 0x007b:
			t.showMenu(window)
		}
	case wmPublish:
		for {
			select {
			case q := <-t.notifications:
				err := t.publishNative(window, q.notification)
				if err != nil {
					t.failures.Add(1)
				}
				q.result <- err
			default:
				return 0
			}
		}
	case wmClose:
		destroyWindow.Call(window)
	case wmDestroy:
		t.available.Store(false)
		d := t.iconData(window)
		t.notify(nimDelete, &d)
		postQuitMessage.Call(0)
	default:
		r, _, _ := defWindowProc.Call(window, message, wparam, lparam)
		return r
	}
	return 0
}
func (t *WindowsTray) queueCommand(command TrayCommand) {
	select {
	case t.commands <- command:
	default:
		t.failures.Add(1)
	}
}
func (t *WindowsTray) runCommands() {
	for {
		select {
		case <-t.done:
			return
		case c := <-t.commands:
			func() {
				defer func() {
					if recover() != nil {
						t.failures.Add(1)
					}
				}()
				t.route.Execute(c)
			}()
		}
	}
}

type trayMenuItem struct {
	Command TrayCommand
	Text    string
}

func trayMenu(paused bool) []trayMenuItem {
	toggle := "Приостановить уведомления"
	if paused {
		toggle = "Возобновить уведомления"
	}
	return []trayMenuItem{{OpenApplication, "Открыть LLM Inspector"}, {OpenNotificationSettings, "Настройки уведомлений"}, {ToggleNotifications, toggle}, {0, ""}, {Exit, "Выход"}}
}
func (t *WindowsTray) showMenu(window uintptr) {
	menu, _, _ := createPopupMenu.Call()
	if menu == 0 {
		t.failures.Add(1)
		return
	}
	defer destroyMenu.Call(menu)
	paused := false
	if t.paused != nil {
		paused = t.paused()
	}
	for _, item := range trayMenu(paused) {
		if item.Command == 0 {
			appendMenu.Call(menu, 0x800, 0, 0)
			continue
		}
		label, _ := windows.UTF16PtrFromString(item.Text)
		appendMenu.Call(menu, 0, uintptr(item.Command), uintptr(unsafe.Pointer(label)))
	}
	var position point
	ok, _, _ := getCursorPos.Call(uintptr(unsafe.Pointer(&position)))
	if ok == 0 {
		return
	}
	setForegroundWindow.Call(window)
	command, _, _ := trackPopupMenu.Call(menu, 0x100|2, uintptr(position.X), uintptr(position.Y), 0, window, 0)
	if command >= uintptr(OpenApplication) && command <= uintptr(Exit) {
		t.queueCommand(TrayCommand(command))
	}
	postMessage.Call(window, 0, 0, 0)
}
func (t *WindowsTray) publishNative(window uintptr, n Notification) error {
	d := t.iconData(window)
	d.Flags = 0x10
	d.InfoFlags = 1 | 0x80
	if n.Silent {
		d.InfoFlags |= 0x10
	}
	copyUTF16(d.InfoTitle[:], n.Title)
	copyUTF16(d.Info[:], n.Body)
	if !t.notify(nimModify, &d) {
		return errors.New("Windows не подтвердил приём уведомления")
	}
	return nil
}
func copyUTF16(destination []uint16, text string) {
	if len(destination) == 0 {
		return
	}
	clear(destination)
	offset := 0
	for _, r := range text {
		if r == 0 || r < ' ' || r == 127 {
			r = ' '
		}
		encoded := utf16.Encode([]rune{r})
		if offset+len(encoded) >= len(destination) {
			break
		}
		copy(destination[offset:], encoded)
		offset += len(encoded)
	}
}
