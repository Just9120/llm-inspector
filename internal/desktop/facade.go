package desktop

import (
	"context"
	"errors"
	"sync"
	"time"

	"github.com/Just9120/llm-inspector/internal/artifact"
	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/lifecycle"
	"github.com/Just9120/llm-inspector/internal/remote"
)

var ErrNotReady = errors.New("приложение ещё не готово или завершает работу")
var ErrHistory = errors.New("локальная история недоступна")
var ErrPreview = errors.New("предпросмотр устарел; создайте и подтвердите новый")

// Dialogs is implemented by the Wails host, not JavaScript. The bound facade
// never accepts arbitrary save paths, command lines, shell text or credentials.
type Dialogs struct {
	OpenExecutable func() (string, error)
	OpenModel      func() (string, error)
	SaveJSON       func(string) (string, error)
	Hide           func()
	Exit           func()
}

type Facade struct {
	engine  func() *Engine
	dialogs Dialogs
	mu      sync.Mutex
	clear   *history.ClearPreview
	preview *artifact.Artifact
}

func NewFacade(engine func() *Engine, dialogs Dialogs) *Facade {
	return &Facade{engine: engine, dialogs: dialogs}
}
func (f *Facade) current() (*Engine, error) {
	if f.engine == nil {
		return nil, ErrNotReady
	}
	e := f.engine()
	if e == nil || e.Context().Err() != nil {
		return nil, ErrNotReady
	}
	return e, nil
}
func (f *Facade) history() (*history.Store, context.Context, context.CancelFunc, error) {
	e, err := f.current()
	if err != nil {
		return nil, nil, nil, err
	}
	if e.History == nil {
		return nil, nil, nil, ErrHistory
	}
	ctx, cancel := context.WithTimeout(e.Context(), 15*time.Second)
	return e.History, ctx, cancel, nil
}
func historyError(err error) error {
	if err == nil {
		return nil
	}
	if errors.Is(err, history.ErrTooLarge) {
		return errors.New("слишком большой период: сузьте диапазон; неполные результаты не выдаются за полные")
	}
	if errors.Is(err, history.ErrInvalid) {
		return errors.New("проверьте период, фильтры и идентификаторы")
	}
	return errors.New("операция с историей не завершена; исходные данные не заменялись")
}

func (f *Facade) GetState() (ViewState, error) {
	e, err := f.current()
	if err != nil {
		return ViewState{}, err
	}
	return e.Snapshot(), nil
}
func (f *Facade) GetHistory(filter history.Filter) (history.Requests, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.Requests{}, err
	}
	defer cancel()
	value, err := s.Query(ctx, filter)
	return value, historyError(err)
}
func (f *Facade) GetHistoryDetails(filter history.Filter) (history.Slice, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.Slice{}, err
	}
	defer cancel()
	value, err := s.Slice(ctx, filter)
	return value, historyError(err)
}
func (f *Facade) GetOperation(id string) (*history.OperationDetail, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return nil, err
	}
	defer cancel()
	value, err := s.Operation(ctx, id)
	return value, historyError(err)
}
func (f *Facade) Analyze(filter history.Filter) (history.Analytics, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.Analytics{}, err
	}
	defer cancel()
	value, err := s.Analyze(ctx, filter)
	return value, historyError(err)
}
func (f *Facade) Compare(baseline, candidate history.Filter, metric string) (history.Comparison, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.Comparison{}, err
	}
	defer cancel()
	value, err := s.Compare(ctx, baseline, candidate, metric)
	return value, historyError(err)
}
func (f *Facade) GetRetention() (history.Retention, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return "", err
	}
	defer cancel()
	value, err := s.Retention(ctx)
	return value, historyError(err)
}
func (f *Facade) SetRetention(value history.Retention) (int, error) {
	s, ctx, cancel, err := f.history()
	if err != nil {
		return 0, err
	}
	defer cancel()
	if err = s.SetRetention(ctx, value); err != nil {
		return 0, historyError(err)
	}
	f.mu.Lock()
	f.clear = nil
	f.mu.Unlock()
	count, err := s.ApplyRetention(ctx, value, time.Now())
	return count, historyError(err)
}

func (f *Facade) PreviewClear(scope history.ClearScope) (history.ClearPreview, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.clear = nil
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.ClearPreview{}, err
	}
	defer cancel()
	preview, err := s.PreviewClear(ctx, scope)
	if err != nil {
		return history.ClearPreview{}, historyError(err)
	}
	f.clear = &preview
	return preview, nil
}
func (f *Facade) ConfirmClear(token string, confirmed bool) (history.ClearPreview, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	if !confirmed || f.clear == nil || token == "" || token != f.clear.Token {
		return history.ClearPreview{}, ErrPreview
	}
	preview := *f.clear
	f.clear = nil
	s, ctx, cancel, err := f.history()
	if err != nil {
		return history.ClearPreview{}, err
	}
	defer cancel()
	value, err := s.Clear(ctx, preview, true)
	return value, historyError(err)
}

func (f *Facade) PreviewSnapshot(selection artifact.Selection) (artifact.Artifact, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.preview = nil
	s, ctx, cancel, err := f.history()
	if err != nil {
		return artifact.Artifact{}, err
	}
	defer cancel()
	e := f.engine()
	if e == nil {
		return artifact.Artifact{}, ErrNotReady
	}
	value, err := artifact.CreateSnapshot(ctx, s, selection, artifact.EnvironmentFromVersions(e.facts.OSVersion, "", "", ""), time.Now())
	if err != nil {
		return artifact.Artifact{}, historyError(err)
	}
	f.preview = &value
	return value, nil
}
func (f *Facade) PreviewExport(from, to time.Time) (artifact.Artifact, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.preview = nil
	s, ctx, cancel, err := f.history()
	if err != nil {
		return artifact.Artifact{}, err
	}
	defer cancel()
	value, err := artifact.CreateExport(ctx, s, from, to, time.Now())
	if err != nil {
		return artifact.Artifact{}, historyError(err)
	}
	f.preview = &value
	return value, nil
}
func (f *Facade) SavePreview(sha256 string) (bool, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.preview == nil || sha256 == "" || sha256 != f.preview.SHA256 || f.dialogs.SaveJSON == nil {
		return false, ErrPreview
	}
	e, err := f.current()
	if err != nil {
		return false, err
	}
	path, err := f.dialogs.SaveJSON("llm-inspector-" + time.Now().UTC().Format("20060102-150405") + ".json")
	if err != nil {
		return false, errors.New("не удалось открыть диалог сохранения")
	}
	if path == "" {
		return false, nil
	}
	if err = artifact.Save(e.Context(), *f.preview, path); err != nil {
		return false, errors.New("файл не сохранён: проверьте локальный JSON path и права доступа")
	}
	return true, nil
}

func (f *Facade) SaveSettings(settings background.Settings) error {
	e, err := f.current()
	if err != nil {
		return err
	}
	return e.SaveSettings(settings)
}
func (f *Facade) ToggleNotifications() (bool, error) {
	e, err := f.current()
	if err != nil {
		return false, err
	}
	return e.ToggleNotifications(), nil
}
func (f *Facade) HideWindow() {
	if f.dialogs.Hide != nil {
		f.dialogs.Hide()
	}
}
func (f *Facade) Exit() {
	if f.dialogs.Exit != nil {
		f.dialogs.Exit()
	}
}

func (f *Facade) manager(backend lifecycle.Backend) (*lifecycle.Manager, context.Context, error) {
	e, err := f.current()
	if err != nil {
		return nil, nil, err
	}
	m := e.Lifecycle[backend]
	if m == nil {
		return nil, nil, lifecycle.ErrUnsupported
	}
	return m, e.Context(), nil
}
func (f *Facade) GetLifecycle(backend lifecycle.Backend) (lifecycle.Snapshot, error) {
	m, _, err := f.manager(backend)
	if err != nil {
		return lifecycle.Snapshot{}, err
	}
	return m.Refresh(), nil
}
func (f *Facade) GetLifecycleParameters(backend lifecycle.Backend) ([]lifecycle.Parameter, error) {
	return lifecycle.Profile(backend)
}
func (f *Facade) DiscoverBackend(backend lifecycle.Backend, manualPath string) (lifecycle.Snapshot, error) {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return lifecycle.Snapshot{}, err
	}
	return m.Discover(ctx, manualPath)
}
func (f *Facade) ChooseExecutable() (string, error) {
	if f.dialogs.OpenExecutable == nil {
		return "", ErrNotReady
	}
	return f.dialogs.OpenExecutable()
}
func (f *Facade) ChooseModel() (string, error) {
	if f.dialogs.OpenModel == nil {
		return "", ErrNotReady
	}
	return f.dialogs.OpenModel()
}
func (f *Facade) ConfirmBackend(backend lifecycle.Backend, token string) error {
	m, _, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.Confirm(token)
}
func (f *Facade) SetBackendParameter(backend lifecycle.Backend, id, value string) error {
	m, _, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.SetParameter(id, value)
}
func (f *Facade) SetBackendParameters(backend lifecycle.Backend, values map[string]string) error {
	m, _, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.SetParameters(values)
}
func (f *Facade) ResetBackendParameters(backend lifecycle.Backend) error {
	m, _, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.ResetParameters()
}
func (f *Facade) StartBackend(backend lifecycle.Backend) error {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.Start(ctx)
}
func (f *Facade) StopBackend(backend lifecycle.Backend) error {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.Stop(ctx)
}
func (f *Facade) RestartBackend(backend lifecycle.Backend) error {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.Restart(ctx)
}
func (f *Facade) GetModels(backend lifecycle.Backend) ([]string, error) {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return nil, err
	}
	return m.Models(ctx)
}
func (f *Facade) LoadModel(backend lifecycle.Backend, model string) error {
	m, ctx, err := f.manager(backend)
	if err != nil {
		return err
	}
	return m.LoadModel(ctx, model)
}

func (f *Facade) EnableRemote(confirmed bool) (remote.Change, error) {
	e, err := f.current()
	if err != nil {
		return remote.Change{}, err
	}
	if e.Remote == nil {
		return remote.Change{}, remote.ErrUnavailable
	}
	return e.Remote.Enable(e.Context(), confirmed)
}
func (f *Facade) RotateRemoteToken(confirmed bool) (remote.Change, error) {
	e, err := f.current()
	if err != nil {
		return remote.Change{}, err
	}
	if e.Remote == nil {
		return remote.Change{}, remote.ErrUnavailable
	}
	return e.Remote.Rotate(e.Context(), confirmed)
}
func (f *Facade) DisableRemote() (remote.Change, error) {
	e, err := f.current()
	if err != nil {
		return remote.Change{}, err
	}
	if e.Remote == nil {
		return remote.Change{}, remote.ErrUnavailable
	}
	return e.Remote.Disable(e.Context())
}
func (f *Facade) ProbeRemoteBackend() (gateway.RemoteBackendStatus, error) {
	e, err := f.current()
	if err != nil {
		return gateway.RemoteBackendStatus{}, err
	}
	if e.RemoteBackend == nil {
		return gateway.RemoteBackendStatus{}, errors.New("remote backend не настроен параметрами запуска")
	}
	return e.RemoteBackend.Probe(e.Context())
}
