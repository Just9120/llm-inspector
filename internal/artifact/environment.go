package artifact

import (
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
)

// Enrich only missing facts from the exact selected history. Multiple observed
// versions are not collapsed into an arbitrary current version. A version may
// be unavailable for some samples; this source describes observed, not uniform,
// environment evidence. No live probing or configuration/path export occurs.
func withRecordedVersions(env Environment, selected history.Slice) Environment {
	drivers, backends, clients := []string{}, []string{}, []string{}
	for _, sample := range selected.Resources {
		drivers = append(drivers, sample.GPUDriverVersion)
	}
	for _, request := range selected.Requests {
		if request.Runtime == nil {
			continue
		}
		drivers = append(drivers, request.Runtime.GPUDriverVersion)
		backends = append(backends, request.Runtime.BackendVersion)
		clients = append(clients, request.Runtime.ClientVersion)
	}
	merge := func(original Fact, values []string) Fact {
		if original.Availability != "unavailable" {
			return original
		}
		unique := ""
		for _, value := range values {
			if value == "" {
				continue
			}
			if domain.TechnicalIdentifier(value) == "" || unique != "" && unique != value {
				return fact("", "history-observed-version-v1")
			}
			unique = value
		}
		return fact(unique, "history-observed-version-v1")
	}
	env.GPUDriver = merge(env.GPUDriver, drivers)
	env.Backend = merge(env.Backend, backends)
	env.Client = merge(env.Client, clients)
	return env
}
