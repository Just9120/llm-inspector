# LM Studio native chat fixture contract v1

Synthetic fixtures model the documented `POST /api/v1/chat` response and named SSE events. They contain no real user content.

- `model_load_time_seconds` is present only for a cold request.
- A complete response with `stats` and no load field/event is a warm request.
- Streaming cold classification requires an exact `model_load.end.load_time_seconds` value.
- Missing or malformed terminal `stats` remains unavailable.

Source contract: <https://lmstudio.ai/docs/developer/rest/chat> and <https://lmstudio.ai/docs/developer/rest/streaming-events>.
