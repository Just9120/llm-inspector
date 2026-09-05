package resources

import (
	"fmt"
	"math"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestResourceProjectionQualityAndCounterBoundaries(t *testing.T) {
	at := time.Now()
	p := Snapshot{CapturedAt: at, CPUAvailable: true, Idle: 100, Kernel: 200, User: 100, Process: &ProcessSnapshot{CPU100ns: 10, ReadBytes: 10, WriteBytes: 20}}
	c := Snapshot{CapturedAt: at.Add(time.Second), CPUAvailable: true, Idle: 150, Kernel: 250, User: 150, MemoryAvailable: true, TotalMemory: 1000, AvailableMemory: 250, Process: &ProcessSnapshot{CPU100ns: 10000010, WorkingSet: 100, ReadBytes: 25, WriteBytes: 40}}
	association := &domain.ProcessAssociation{PID: 42, StartedAt: at.Add(-time.Hour), ImageName: "ollama.exe", SourceVersion: "test-v1"}
	context := RequestContext{RequestID: "12341234123412341234123412341234"}
	stage := domain.StageValue{Stage: domain.Generating, Evidence: "protocol_observed", SourceVersion: "test-v1"}
	r := Project(context, stage, association, &p, &c, Traffic{Sent: 100, Received: 200}, true, 2, at)[0]
	for _, tc := range []struct {
		m domain.Metric
		v float64
		q domain.Quality
	}{{r.CPU, 50, domain.Calculated}, {r.MemoryPercent, 75, domain.Calculated}, {r.MemoryUsed, 750, domain.Calculated}, {r.ProcessCPU, 50, domain.Calculated}, {r.ProcessMemory, 100, domain.Exact}, {r.DiskRead, 15, domain.Calculated}, {r.DiskWrite, 20, domain.Calculated}, {r.ClientToBackend, 100, domain.Exact}, {r.BackendToClient, 200, domain.Exact}} {
		if tc.m.Validate() != nil || tc.m.Value == nil || *tc.m.Value != tc.v || tc.m.Quality != tc.q {
			t.Fatal(tc)
		}
	}
	if r.RequestID != context.RequestID || r.Process == nil || r.GPUUtilization.Value != nil {
		t.Fatal(r)
	}
	first := Project(context, stage, association, nil, &c, Traffic{}, true, 2, at)[0]
	if first.CPU.Value != nil || first.ProcessCPU.Value != nil || first.DiskRead.Value != nil || first.MemoryUsed.Value == nil {
		t.Fatal("first sample fabricated delta")
	}
	c.Kernel = 1
	c.Process.ReadBytes = 1
	c.Process.CPU100ns = 1
	regressed := Project(context, stage, association, &p, &c, Traffic{}, true, 2, at)[0]
	if regressed.CPU.Value != nil || regressed.DiskRead.Value != nil || regressed.ProcessCPU.Value != nil {
		t.Fatal("counter regression fabricated")
	}
	c.CPUAvailable = false
	c.MemoryAvailable = false
	missing := Project(context, stage, nil, &p, &c, Traffic{}, true, 2, at)[0]
	if missing.CPU.Value != nil || missing.MemoryPercent.Value != nil || missing.Process != nil || missing.ProcessMemory.Value != nil {
		t.Fatal("missing source fabricated")
	}
}

func TestGPUIsolationAndRemoteAttribution(t *testing.T) {
	at := time.Now()
	v := 12.0
	c := Snapshot{CapturedAt: at, MemoryAvailable: true, TotalMemory: 1000, AvailableMemory: 500, GPUs: []GPU{{ID: "GPU-one", Driver: "580.1", Utilization: &v, UsedMiB: &v}, {ID: "GPU-two", Utilization: &v}}}
	local := Project(RequestContext{}, domain.StageValue{}, nil, nil, &c, Traffic{Sent: 1, Received: 2}, true, 1, at)
	if len(local) != 2 || local[0].MemoryUsed.Value == nil || local[1].MemoryUsed.Value != nil || local[1].ClientToBackend.Value != nil || local[0].GPUVRAMUsed.Quality != domain.Calculated || *local[0].GPUVRAMUsed.Value != 12*1048576 {
		t.Fatal("multi-GPU double counts host metrics")
	}
	remote := Project(RequestContext{}, domain.StageValue{}, nil, nil, &c, Traffic{Sent: 1, Received: 2}, false, 1, at)
	if len(remote) != 1 || remote[0].MemoryPercent.Value != nil || remote[0].GPUUtilization.Value != nil || *remote[0].ClientToBackend.Value != 1 {
		t.Fatal("remote backend received local host telemetry")
	}
	if systemCPU(Snapshot{Kernel: math.MaxUint64}, Snapshot{Kernel: 1}).Value != nil {
		t.Fatal("uint counter wrapped")
	}
}

func TestNvidiaCSVBoundedIndependentFields(t *testing.T) {
	text := "1, GPU-b, 580.1, 110, N/A, 24576, 60, 80.5\r\n0, GPU-a, N/A, 12, 100, 24576, [Not Supported], 20\n0, GPU-other, 580.1, 1, 1, 1, 1, 1\n2, GPU-a, 580.1, 1, 1, 1, 1, 1\n3, PRIVATE TEXT, 1, 1, 1, 1, 1, 1\n"
	gpus := ParseNvidiaCSV(text)
	if len(gpus) != 2 || gpus[0].ID != "GPU-a" || gpus[0].Driver != "" || gpus[0].Temperature != nil || gpus[1].UsedMiB != nil {
		t.Fatal(gpus)
	}
	if optional(gpus[1].Utilization, domain.Percent).Value != nil {
		t.Fatal("invalid utilization not unavailable")
	}
	for _, v := range []string{"NaN", "Inf", "-1", "1e3", "1,2", "", "1.2.3"} {
		if number(v) != nil {
			t.Fatal(v)
		}
	}
	var b strings.Builder
	for i := 20; i >= 0; i-- {
		fmt.Fprintf(&b, "%d, GPU-%d, 580.1, 1, 1, 1, 1, 1\n", i, i)
	}
	if g := ParseNvidiaCSV(b.String()); len(g) != 16 || g[0].Index != 0 || g[15].Index != 15 {
		t.Fatal("device cap/order")
	}
	if len(ParseNvidiaCSV(strings.Repeat("x", 16*1024+1))) != 0 {
		t.Fatal("output bound ignored")
	}
}
