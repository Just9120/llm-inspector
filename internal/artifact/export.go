package artifact

import (
	"context"
	"sort"
	"time"
	"unicode"

	"github.com/Just9120/llm-inspector/internal/history"
)

type ExportSelection struct {
	From time.Time `json:"from_utc"`
	To   time.Time `json:"to_utc"`
}
type ExportHistory struct {
	Requests  []Request  `json:"requests"`
	Resources []Resource `json:"resource_samples"`
}
type ExportMetric struct {
	Category string `json:"category"`
	Key      string `json:"key"`
	Unit     string `json:"unit"`
	history.Aggregate
}
type ExportTrend struct {
	Day     string         `json:"day"`
	Metrics []ExportMetric `json:"metrics"`
}
type Export struct {
	Schema      string             `json:"schema_version"`
	GeneratedAt time.Time          `json:"generated_at_utc"`
	Selection   ExportSelection    `json:"selection"`
	History     ExportHistory      `json:"history"`
	Aggregates  []ExportTrend      `json:"aggregate_metrics"`
	ModelLoads  history.ModelLoads `json:"model_loads"`
}

func CreateExport(ctx context.Context, store *history.Store, from, to, now time.Time) (Artifact, error) {
	if from.After(to) {
		return Artifact{}, history.ErrInvalid
	}
	f, t := from.UTC(), to.UTC()
	slice, err := store.Slice(ctx, history.Filter{From: &f, To: &t})
	if err != nil {
		return Artifact{}, err
	}
	if slice.RequestsTruncated || slice.ResourcesTruncated {
		return Artifact{}, history.ErrTooLarge
	}
	requests, resources, err := project(slice)
	if err != nil {
		return Artifact{}, err
	}
	type key struct{ category, name, unit string }
	buckets := map[string]map[key][]float64{}
	add := func(at time.Time, k key, value *float64) {
		if value == nil {
			return
		}
		day := at.UTC().Format("2006-01-02")
		if buckets[day] == nil {
			buckets[day] = map[key][]float64{}
		}
		buckets[day][k] = append(buckets[day][k], *value)
	}
	loads := history.ModelLoads{}
	for _, r := range requests {
		for _, m := range r.Metrics {
			add(r.StartedAt, key{"request", snake(m.Key), m.Unit}, m.Value)
		}
		rate := 0.0
		if r.ErrorType != "none" {
			rate = 100
		}
		add(r.StartedAt, key{"request", "error_rate_percent", "percent"}, &rate)
		switch r.ModelLoad {
		case "cold":
			loads.Cold++
		case "warm":
			loads.Warm++
		default:
			loads.Unavailable++
		}
	}
	for _, r := range resources {
		for _, m := range r.Metrics {
			add(r.CapturedAt, key{"resource", m.Key, m.Unit}, m.Value)
		}
	}
	trend := []ExportTrend{}
	for day, bucket := range buckets {
		entry := ExportTrend{Day: day, Metrics: []ExportMetric{}}
		for k, values := range bucket {
			entry.Metrics = append(entry.Metrics, ExportMetric{Category: k.category, Key: k.name, Unit: k.unit, Aggregate: history.Calculate(values)})
		}
		sort.Slice(entry.Metrics, func(i, j int) bool {
			a, b := entry.Metrics[i], entry.Metrics[j]
			return a.Category+":"+a.Key+":"+a.Unit < b.Category+":"+b.Key+":"+b.Unit
		})
		trend = append(trend, entry)
	}
	sort.Slice(trend, func(i, j int) bool { return trend[i].Day < trend[j].Day })
	return encode(Export{Schema: "analytics-export-v1", GeneratedAt: now.UTC(), Selection: ExportSelection{From: f, To: t}, History: ExportHistory{Requests: requests, Resources: resources}, Aggregates: trend, ModelLoads: loads})
}
func snake(s string) string {
	out := []rune{}
	for i, c := range s {
		if unicode.IsUpper(c) {
			if i > 0 {
				out = append(out, '_')
			}
			c = unicode.ToLower(c)
		}
		out = append(out, c)
	}
	return string(out)
}
