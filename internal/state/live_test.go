package state

import (
	"fmt"
	"sync"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestLiveStageElapsedAndTerminalIsolation(t *testing.T) {
	now := time.Unix(0, 0)
	l := NewLive(func() time.Time { return now })
	l.Start("first", domain.Cline, now)
	l.Start("second", domain.OpenWebUI, now)
	now = now.Add(1250 * time.Millisecond)
	for _, stage := range []domain.Stage{domain.ModelLoading, domain.QueueWaiting, domain.PromptProcessing, domain.Generating, domain.ToolWait} {
		l.Stage("first", domain.StageValue{Stage: stage, Evidence: "backend_reported", SourceVersion: "backend-events-v1"})
		got := l.Snapshot().Active[0]
		if got.Stage.Stage != stage || got.Elapsed.Value == nil || *got.Elapsed.Value != 1250 || got.Progress.Value != nil || got.ETA.Value != nil {
			t.Fatal("live stage/provenance drift")
		}
	}
	l.Finish("first", "completed", "http_api_error")
	got := l.Snapshot()
	if len(got.Active) != 1 || got.Active[0].RequestID != "second" || got.LatestTerminal.Stage.Stage != domain.Failed {
		t.Fatal("terminal request contaminated another request")
	}
	l.Finish("second", "client_cancelled", "client_cancellation")
	if l.Snapshot().LatestTerminal.Stage.Stage != domain.Cancelled {
		t.Fatal("cancellation missing")
	}
}

func TestETAReliesOnlyOnSufficientMonotonicBackendEvidence(t *testing.T) {
	now := time.Unix(0, 0)
	l := NewLive(func() time.Time { return now })
	l.Start("request", domain.Generic, now)
	progress := func(v float64, source string) {
		l.Progress("request", domain.Measured(v, domain.Percent, "backend_extension", source))
	}
	progress(10, "v1")
	now = now.Add(time.Second)
	progress(20, "v1")
	if l.Snapshot().Active[0].ETA.Value != nil {
		t.Fatal("insufficient ETA")
	}
	now = now.Add(time.Second)
	progress(30, "v1")
	got := l.Snapshot().Active[0]
	if got.ETA.Quality != domain.Estimated || *got.ETA.Value != 7000 {
		t.Fatal("ETA derivation")
	}
	*got.Progress.Value = 99
	if *l.Snapshot().Active[0].Progress.Value != 30 {
		t.Fatal("snapshot aliases state")
	}
	progress(25, "v1")
	if l.Snapshot().Active[0].ETA.Value != nil {
		t.Fatal("regression retained ETA")
	}
	progress(40, "v2")
	if l.Snapshot().Active[0].ETA.Value != nil {
		t.Fatal("source change retained ETA")
	}
	l.Progress("request", domain.Measured(50, domain.Percent, "inspector", "v2"))
	if *l.Snapshot().Active[0].Progress.Value != 40 {
		t.Fatal("nonbackend percentage accepted")
	}
	l.Finish("request", "completed", "none")
	if l.Snapshot().LatestTerminal.ETA.Value != nil {
		t.Fatal("terminal ETA not cleared")
	}
}

func TestLiveBoundedConcurrentUpdates(t *testing.T) {
	l := NewLive(nil)
	var wg sync.WaitGroup
	for i := 0; i < MaxActiveRequests+10; i++ {
		i := i
		wg.Go(func() { l.Start(fmt.Sprint(i), domain.Generic, time.Now()) })
	}
	wg.Wait()
	got := l.Snapshot()
	if len(got.Active) != MaxActiveRequests || got.Omitted != 10 {
		t.Fatal("active state not bounded")
	}
	for _, r := range got.Active {
		wg.Go(func() {
			l.Stage(r.RequestID, ProtocolStage(domain.Generating))
			l.Finish(r.RequestID, "completed", "none")
		})
	}
	wg.Wait()
	if len(l.Snapshot().Active) != 0 {
		t.Fatal("terminal leak")
	}
}
