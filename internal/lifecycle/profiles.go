package lifecycle

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"math"
	"net/url"
	"path/filepath"
	"strconv"
	"strings"
	"unicode"
)

type Parameter struct {
	ID      string   `json:"id"`
	Label   string   `json:"label"`
	Hint    string   `json:"hint"`
	Default string   `json:"default"`
	Minimum int      `json:"minimum"`
	Maximum int      `json:"maximum"`
	Choices []string `json:"choices,omitempty"`
}

func Profile(backend Backend) ([]Parameter, error) {
	port := 0
	var extra []Parameter
	switch backend {
	case Ollama:
		port = 11434
		extra = []Parameter{{ID: "keep-alive", Label: "Хранить модель в памяти", Hint: "Секунды; пусто — настройка backend", Minimum: 0, Maximum: 604800}, {ID: "parallel", Label: "Параллельные запросы", Minimum: 1, Maximum: 64}, {ID: "max-loaded", Label: "Моделей в памяти", Minimum: 1, Maximum: 64}, {ID: "max-queue", Label: "Длина очереди", Minimum: 1, Maximum: 65536}}
	case LlamaCpp:
		port = 8080
		extra = []Parameter{{ID: "gpu-layers", Label: "Слои на GPU", Hint: "Авто, выкл., все или число 0–999", Choices: []string{"auto", "off", "all"}}, {ID: "cpu-threads", Label: "Потоки CPU", Minimum: 1, Maximum: 512}, {ID: "parallel", Label: "Параллельные слоты", Minimum: 1, Maximum: 64}}
	case LMStudio:
		port = 1234
		extra = []Parameter{{ID: "gpu-offload", Label: "Доля модели на GPU", Hint: "Авто, выкл., максимум или доля 0–1", Choices: []string{"auto", "off", "max"}}, {ID: "model-ttl", Label: "Время жизни модели", Hint: "Секунды", Minimum: 0, Maximum: 604800}, {ID: "model-id", Label: "Ключ модели", Hint: "Точный ключ локальной модели"}}
	default:
		return nil, ErrUnsupported
	}
	return append([]Parameter{{ID: "local-port", Label: "Локальный порт", Default: strconv.Itoa(port), Minimum: 1024, Maximum: 65535}, {ID: "context", Label: "Размер контекста", Hint: "Токены; пусто — настройка backend", Minimum: 128, Maximum: 1048576}}, extra...), nil
}

func Defaults(backend Backend) map[string]string {
	p, _ := Profile(backend)
	m := make(map[string]string, len(p))
	for _, item := range p {
		m[item.ID] = item.Default
	}
	return m
}

func Normalize(backend Backend, id, value string) (string, error) {
	profile, err := Profile(backend)
	if err != nil {
		return "", err
	}
	var param *Parameter
	for i := range profile {
		if profile[i].ID == id {
			param = &profile[i]
			break
		}
	}
	if param == nil || len(value) > 512 || strings.IndexFunc(value, unicode.IsControl) >= 0 {
		return "", ErrParameter
	}
	value = strings.TrimSpace(value)
	if value == "" {
		return param.Default, nil
	}
	switch id {
	case "model-id":
		if !validModel(value) {
			return "", ErrParameter
		}
		return value, nil
	case "gpu-layers":
		if value == "auto" || value == "off" || value == "all" {
			return value, nil
		}
		n, err := unsignedInteger(value)
		if err != nil || n > 999 {
			return "", ErrParameter
		}
		return strconv.Itoa(n), nil
	case "gpu-offload":
		if value == "auto" || value == "off" || value == "max" {
			return value, nil
		}
		// Locale-independent decimal only; NaN/Inf/exponents and flags are not accepted.
		if strings.Trim(value, "0123456789.") != "" {
			return "", ErrParameter
		}
		n, err := strconv.ParseFloat(value, 64)
		if err != nil || math.IsNaN(n) || math.IsInf(n, 0) || n < 0 || n > 1 {
			return "", ErrParameter
		}
		return strconv.FormatFloat(n, 'f', -1, 64), nil
	default:
		n, err := unsignedInteger(value)
		if err != nil || n < param.Minimum || n > param.Maximum {
			return "", ErrParameter
		}
		return strconv.Itoa(n), nil
	}
}

func unsignedInteger(value string) (int, error) {
	if value == "" || strings.Trim(value, "0123456789") != "" {
		return 0, ErrParameter
	}
	return strconv.Atoi(value)
}
func validModel(value string) bool {
	return value != "" && len(value) <= 1024 && !strings.HasPrefix(value, "-") && strings.IndexFunc(value, unicode.IsControl) < 0 && strings.TrimSpace(value) == value
}
func localFile(path, extension string) bool {
	return filepath.IsAbs(path) && !strings.HasPrefix(path, `\\`) && !strings.HasPrefix(path, "//") && strings.EqualFold(filepath.Ext(path), extension) && strings.IndexAny(path, "\x00\r\n\"") < 0
}
func endpoint(backend Backend, parameters map[string]string) string {
	return "http://127.0.0.1:" + parameters["local-port"] + "/"
}
func validEndpoint(raw string) bool {
	u, err := url.Parse(raw)
	return err == nil && u.Scheme == "http" && u.Hostname() == "127.0.0.1" && u.Port() != "" && u.User == nil && u.RawQuery == "" && u.Fragment == "" && (u.Path == "" || u.Path == "/")
}
func confirmation(target Target) string {
	s := sha256.Sum256([]byte(fmt.Sprintf("%s\x00%s\x00%s\x00%s", target.Backend, target.Executable, target.Version, target.Endpoint)))
	return hex.EncodeToString(s[:])
}
