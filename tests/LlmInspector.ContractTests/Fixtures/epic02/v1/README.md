# EPIC-02 fixture set v1

Синтетические fixtures фиксируют только документированный OpenAI-compatible subset и не содержат пользовательские данные.

Источники, сверенные 2026-09-02:

- Ollama OpenAI compatibility: <https://docs.ollama.com/api/openai-compatibility>
- Ollama OpenAI adapter usage mapping: <https://github.com/ollama/ollama/blob/main/openai/openai.go>
- llama.cpp server Chat Completions, `usage`, `timings` и tool calls: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>
- LM Studio OpenAI compatibility: <https://lmstudio.ai/docs/developer/openai-compat>
- LM Studio tool use: <https://lmstudio.ai/docs/developer/openai-compat/tools>

Fixture version описывает parser contract, а не обещание совместимости с любой будущей backend version. Неизвестные fields проходят через proxy byte-for-byte, но не становятся telemetry без нового versioned fixture и semantic mapping.
