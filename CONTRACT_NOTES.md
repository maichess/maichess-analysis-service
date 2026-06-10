# Contract Notes — Analysis Service

## Whatif position_history limitation

Whatif move validation uses an empty initial `position_history`. Threefold repetition detection
covers only positions within the whatif branch, not positions from the preceding game. This is a
known acceptable limitation for analysis use.

## from-match authorization broadened to include the match creator

`POST /games/from-match/{match_id}` originally authorized only board participants
(`white.user_id` / `black.user_id`). Profile → Past Matches (served by Match Manager)
also lists matches a user only *started* — bot-vs-bot games attributed via `created_by`
— and renders an Analyse button on every row. Those imports were rejected with `403`,
and the client swallowed the error, so the button appeared dead.

**Decision (approved by product owner):** allow the match's creator
(`created_by_user_id`) to import in addition to participants. The contract
(`rest/analysis.md`) and `ImportFromMatchAsync` were updated together. The match
document already carries `created_by_user_id` (written by Match Manager), so no new
fields or cross-service calls were needed.

## Analysis cache per-depth document limit

Cache reads use `Database.List(limit=100)`. With practical engine depth ceilings around 40, this
is safe. If the engine ever exceeds 100 depths for a position, cache reads will silently miss the
deepest entries. Raise the limit if this becomes an issue.

## Analysis over Kafka — implemented (Kafka task `07`)

Analysis session control can run over Kafka instead of the synchronous
`Engine.AnalyzePosition` gRPC stream. **Opt-in via `KAFKA_ENABLED=true`** (staging only; prod keeps
`kafka.enabled: false`, so the gRPC streaming path stays the default and nothing changes there).
No contract change beyond Kafka `01`; the analysis protos already exist.

- **`Maichess.PlatformProtos` bumped `0.4.0 → 0.6.0`** — the version that carries
  `maichess.events.v1` (the analysis `*_commands` / `*_events` protos from task `01`). Restores +
  builds clean; no source changes were required by the bump itself.
- **Producer (`Kafka/KafkaAnalysisCommandSink.cs`, `[ExcludeFromCodeCoverage]`):** publishes
  `StartAnalysis{sessionId, fen, botId, lineCount}` / `StopAnalysis{sessionId}` to
  `analysis.commands.v1` (keyed by sessionId) via the Confluent Protobuf serde
  (`Kafka/ProtobufEventSerdes.cs`). Behind the `Services/IAnalysisCommandSink` seam.
- **Consumer (`Kafka/AnalysisEventConsumer.cs`, `[ExcludeFromCodeCoverage]`):** consumes
  `analysis.events.v1` and forwards depth updates to the client over the existing `Socket.EmitEvent`
  channel (`analysis_update` / `analysis_complete` / `analysis_error`). It runs in-process with the
  producer and resolves the live in-memory session by id (`AnalysisSessionService.FindById`) to
  recover the user and apply the stale-position filter. **Unique consumer group per process** so a
  multi-replica deploy fans every event to every replica and each delivers only the sessions it
  holds (the session is pinned in-memory to the replica that started it).
- **`AnalysisSessionService`:** when a sink is injected it routes start/stop through Kafka. Cached
  depths are still emitted on the command side (`EmitCachedDepthsAsync`) before the `StartAnalysis`
  is published; live depths arrive over `analysis.events.v1`. The session tracks `AnalyzedFen`
  (drops events from a superseded position — navigate/whatif) and `MaxCachedDepth` (drops a live
  depth already served from cache). Cancellation is by `StopAnalysis`; the engine cancels silently,
  so no terminal event is emitted on cancel (matching the gRPC behaviour).

**Verification limit:** built green and the existing game-service tests pass, but the Kafka path was
not exercised end-to-end here (needs Kafka + engine + socket-service running). The new I/O glue is
`[ExcludeFromCodeCoverage]` + Stryker-excluded, consistent with the existing repos/endpoints and the
(already untested) `AnalysisSessionService` orchestration.
