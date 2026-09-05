package domain

// RuntimeFacts contains only allowlisted version/configuration identifiers, never
// command lines, paths, environment values or host/user identities.
type RuntimeFacts struct {
	ConfigurationID  string `json:"configuration_id"`
	InspectorVersion string `json:"inspector_version"`
	FrameworkVersion string `json:"framework_version"`
	OSVersion        string `json:"operating_system_version"`
	TelemetryVersion string `json:"telemetry_contract_version"`
	BackendVersion   string `json:"backend_version,omitempty"`
	ClientVersion    string `json:"client_version,omitempty"`
	ModelVersion     string `json:"model_version,omitempty"`
	GPUDriverVersion string `json:"gpu_driver_version,omitempty"`
}

func (f *RuntimeFacts) Valid() bool {
	if f == nil {
		return true
	}
	if TechnicalIdentifier(f.ConfigurationID) == "" {
		return false
	}
	for _, v := range []string{f.InspectorVersion, f.FrameworkVersion, f.OSVersion, f.TelemetryVersion, f.BackendVersion, f.ClientVersion, f.ModelVersion, f.GPUDriverVersion} {
		if v != "" && TechnicalIdentifier(v) == "" {
			return false
		}
	}
	return true
}
