# OpenAI-compatible Chat Completions fixture set v2

Синтетический fixture расширяет v1 только техническими usage details; пользовательский content отсутствует.

Источники, сверенные 2026-09-03:

- OpenAI Chat Completions `CompletionUsage`: <https://developers.openai.com/api/reference/resources/chat>
- OpenAI model guidance для `usage.prompt_tokens_details.cached_tokens`: <https://developers.openai.com/api/docs/guides/latest-model>
- llama.cpp server `usage` и `timings`: <https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md>

Parser принимает только exact non-negative whole token counts из documented paths. Reasoning content не является telemetry: fixture содержит sentinel рядом с technical counter, а tests доказывают, что он не попадает в projection.
