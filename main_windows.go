//go:build windows

package main

import (
	"embed"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"

	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/winhost"
	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/logger"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	windowsOptions "github.com/wailsapp/wails/v2/pkg/options/windows"
	"golang.org/x/sys/windows"
)

//go:embed all:frontend/dist
var assets embed.FS

func main() { os.Exit(run(os.Args[1:])) }

func run(args []string) int {
	smoke := len(args) == 1 && args[0] == "--smoke-test"
	config, hidden, err := gateway.ParseLaunch(args)
	var fixture *smokeFixture
	var dataDirectory string
	if smoke {
		fixture, err = newSmokeFixture()
		if err == nil {
			config, dataDirectory = fixture.config, fixture.directory
			defer fixture.close()
		}
	} else if err == nil {
		var base string
		base, err = winhost.LocalDataPath()
		dataDirectory = filepath.Join(base, "LLM Inspector")
	}
	if err != nil {
		showStartupError(smoke)
		return 1
	}
	if err := os.MkdirAll(dataDirectory, 0700); err != nil {
		showStartupError(smoke)
		return 1
	}
	executable, err := os.Executable()
	if err != nil {
		showStartupError(smoke)
		return 1
	}
	static, err := fs.Sub(assets, "frontend/dist")
	if err != nil {
		showStartupError(smoke)
		return 1
	}
	host := newHost(config, dataDirectory, executable, fixture)
	host.visible.Store(!hidden && !smoke)
	identity := "92d67763-5085-4858-831a-e319879c85a8"
	if smoke {
		identity += fmt.Sprintf("-%d", os.Getpid())
	}
	err = wails.Run(&options.App{
		Title: "LLM Inspector", Width: 1280, Height: 850, MinWidth: 980, MinHeight: 680,
		StartHidden: hidden || smoke, BackgroundColour: options.NewRGB(16, 21, 30),
		AssetServer: &assetserver.Options{Assets: static},
		Logger:      privateLogger{}, LogLevel: logger.ERROR, LogLevelProduction: logger.ERROR,
		OnStartup: host.startup, OnDomReady: host.domReady, OnBeforeClose: host.beforeClose, OnShutdown: host.shutdown,
		Bind:               []interface{}{host.facade, host},
		SingleInstanceLock: &options.SingleInstanceLock{UniqueId: identity, OnSecondInstanceLaunch: func(options.SecondInstanceData) { host.show(false) }},
		DragAndDrop:        &options.DragAndDrop{DisableWebViewDrop: true},
		Windows: &windowsOptions.Options{
			Theme: windowsOptions.Dark, WebviewUserDataPath: filepath.Join(dataDirectory, "webview2"),
			DisablePinchZoom: true, IsZoomControlEnabled: true,
			DLLSearchPaths: windowsOptions.DLLSearchApplicationDir | windowsOptions.DLLSearchSystem32,
			Messages: &windowsOptions.Messages{
				InstallationRequired: "Для приложения нужен Microsoft Edge WebView2 Runtime.", UpdateRequired: "Обновите Microsoft Edge WebView2 Runtime.",
				MissingRequirements: "Отсутствует необходимый компонент", Webview2NotInstalled: "WebView2 Runtime не установлен", Error: "Ошибка запуска",
				FailedToInstall: "WebView2 Runtime не установлен.", DownloadPage: "Установите Microsoft Edge WebView2 Runtime с сайта Microsoft.", PressOKToInstall: "Подтвердите установку.",
				ContactAdmin:         "Установите Microsoft Edge WebView2 Runtime с сайта Microsoft и повторите запуск. Приложение не устанавливает его автоматически.",
				InvalidFixedWebview2: "Недопустимая конфигурация WebView2 Runtime.", WebView2ProcessCrash: "Процесс WebView2 завершился. Перезапустите LLM Inspector.",
			},
		},
	})
	if err != nil {
		showStartupError(smoke)
		return 1
	}
	if smoke {
		if !host.smokePassed.Load() {
			fmt.Fprintln(os.Stderr, "Go desktop smoke: FAIL")
			return 1
		}
		fmt.Println("Go desktop smoke: PASS (WebView2, Russian frontend, bridge, proxy, SQLite, privacy)")
	}
	return 0
}

func showStartupError(smoke bool) {
	message := "Не удалось запустить LLM Inspector. Проверьте параметры запуска, доступ к локальной папке данных и наличие WebView2 Runtime."
	if smoke {
		fmt.Fprintln(os.Stderr, "Go desktop smoke: initialization failed")
		return
	}
	title, _ := windows.UTF16PtrFromString("LLM Inspector")
	body, _ := windows.UTF16PtrFromString(message)
	windows.MessageBox(0, body, title, windows.MB_OK|windows.MB_ICONERROR)
}

// Bridge errors can include arguments. Never persist or print SDK payloads.
type privateLogger struct{}

func (privateLogger) Print(string)   {}
func (privateLogger) Trace(string)   {}
func (privateLogger) Debug(string)   {}
func (privateLogger) Info(string)    {}
func (privateLogger) Warning(string) {}
func (privateLogger) Error(string)   {}
func (privateLogger) Fatal(string) {
	fmt.Fprintln(os.Stderr, "LLM Inspector: fatal desktop runtime error")
	os.Exit(1)
}
