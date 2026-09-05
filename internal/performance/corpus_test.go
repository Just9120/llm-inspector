package performance

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"slices"
	"testing"
)

func TestFrozenPerformanceCorpusIsPreserved(t *testing.T) {
	data, err := os.ReadFile(filepath.Join("..", "..", "benchmarks", "fixtures", "epic12", "v1", "reference-workloads.json"))
	if err != nil {
		t.Fatal(err)
	}
	hash := sha256.Sum256(data)
	if hex.EncodeToString(hash[:]) != "1c38874fb393cfe094bf1d44a281859c2a6e340b9acb46b86df3b458a41f3aca" {
		t.Fatal("frozen corpus changed")
	}
	var corpus struct {
		Version   string `json:"schema_version"`
		Context   int    `json:"fixed_context_tokens"`
		Workloads []struct {
			ID string `json:"id"`
		} `json:"workloads"`
	}
	if err := json.Unmarshal(data, &corpus); err != nil {
		t.Fatal(err)
	}
	if corpus.Version != "performance-corpus-v1" || corpus.Context != 8192 {
		t.Fatal("corpus contract")
	}
	ids := []string{}
	for _, w := range corpus.Workloads {
		ids = append(ids, w.ID)
	}
	expected := []string{"idle", "cold-load", "hybrid-streaming-c1", "hybrid-nonstreaming-c1", "hybrid-streaming-c4", "cpu-only", "tools-fragmented-stream", "collector-unavailable", "collector-failure"}
	slices.Sort(ids)
	slices.Sort(expected)
	if !slices.Equal(ids, expected) {
		t.Fatal("workload coverage changed")
	}
}
