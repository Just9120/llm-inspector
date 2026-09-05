package resources

import (
	"math"
	"sort"
	"strconv"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

// The native command and parser both cap output; no full untrusted process log
// is accumulated. Unsupported numeric fields stay nil independently.
func ParseNvidiaCSV(output string) []GPU {
	result := []GPU{}
	if len(output) > 16*1024 {
		return result
	}
	seen := map[string]bool{}
	indexes := map[int]bool{}
	for _, line := range strings.Split(output, "\n") {
		fields := strings.Split(strings.TrimSpace(line), ",")
		if len(fields) != 8 {
			continue
		}
		for i := range fields {
			fields[i] = strings.TrimSpace(fields[i])
		}
		n, err := strconv.Atoi(fields[0])
		if err != nil || n < 0 || n > 65535 {
			continue
		}
		id := domain.TechnicalIdentifier(fields[1])
		if id == "" || seen[id] || indexes[n] {
			continue
		}
		seen[id] = true
		indexes[n] = true
		driver := domain.TechnicalIdentifier(fields[2])
		if strings.EqualFold(driver, "N/A") {
			driver = ""
		}
		result = append(result, GPU{Index: n, ID: id, Driver: driver, Utilization: number(fields[3]), UsedMiB: number(fields[4]), TotalMiB: number(fields[5]), Temperature: number(fields[6]), Power: number(fields[7])})
	}
	sort.Slice(result, func(i, j int) bool { return result[i].Index < result[j].Index })
	if len(result) > 16 {
		result = result[:16]
	}
	return result
}
func number(s string) *float64 {
	if s == "" {
		return nil
	}
	for _, c := range s {
		if (c < '0' || c > '9') && c != '.' {
			return nil
		}
	}
	v, err := strconv.ParseFloat(s, 64)
	if err != nil || v < 0 || math.IsNaN(v) || math.IsInf(v, 0) {
		return nil
	}
	return &v
}
