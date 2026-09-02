# EPIC-02 fixture set v1

Синтетические fixtures фиксируют только документированный OpenAI-compatible subset и не содержат пользовательские данные.

Источники, сверенные 2026-09-02:

- Ollama OpenAI compatibility: <https://docs.ollama.com/api/openai-compatibility>
- Ollama OpenAI adapter usage mapping: <https://github.com/ollama/ollama/blob/main/openai/openai.go>
- llama.cpp server Chat Completions, `usage`, `timings` и tool calls: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>
- LM Studio OpenAI compatibility: <https://lmstudio.ai/docs/developer/openai-compat>
- LM Studio tool use: <https://lmstudio.ai/docs/developer/openai-compat/tools>

Fixture version описывает parser contract, а не обещание совместимости с любой будущей backend version. Неизвестные fields проходят через proxy byte-for-byte, но не становятся telemetry без нового versioned fixture и semantic mapping.

Client base URL capability и explicit endpoint attribution сверены с primary documentation/source:

- OpenCode custom provider `baseURL`: <https://dev.opencode.ai/docs/providers>
- Hermes custom/self-hosted `base_url`: <https://github.com/hermes-agent-org/hermes/blob/main/website/docs/integrations/providers.md>
- Cline OpenAI Compatible Base URL: <https://github.com/cline/cline/blob/main/apps/vscode/webview-ui/src/components/settings/providers/OpenAICompatible.tsx>
- Open WebUI OpenAI-compatible connection URLs: <https://github.com/open-webui/open-webui/blob/main/backend/open_webui/routers/openai.py>

Маршрут является explicit user configuration evidence, но не доказывает process identity. Поэтому generic `/v1` всегда получает `Generic/Unknown`, а brand не выводится из `User-Agent`, времени или payload content.
