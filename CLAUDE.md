# Analysis Service

Manages saved analysis games and analysis sessions. Drives engine analysis and pushes results to
clients via the Socket Service.

## Contracts

- **REST (server):** `maichess-api-contracts/rest/analysis.md`
- **gRPC (clients):**
  - `protos/database-service/v1/database.proto` — `Get`, `List`, `Insert`, `DeleteWhere` on `analysis_games`, `analysis_results`, `analysis_meta`; `Get` on `matches` (read-only)
  - `protos/engine-service/v1/bots.proto` — `AnalyzePosition`, `ListBots`
  - `protos/move-validator-service/v1/moves.proto` — `ValidateMoveSan`, `ConvertSequenceToSan`, `ValidateMove`
  - `protos/socket-service/v1/socket.proto` — `EmitEvent`
- **Generated stubs:** `Maichess.PlatformProtos` NuGet (see `maichess-api-contracts/dotnet/`)

The `analysis.proto` gRPC server endpoint has been removed. This service exposes REST only.
No `Matches.MatchesClient` — match data is read directly from match-db via `Database.DatabaseClient`.

Implement against these contracts exactly. Document any blocker in `CONTRACT_NOTES.md`.

## Stack

- **Runtime:** ASP.NET (net10.0), C#, nullable enabled
- **Database:** match-db (MongoDB) via `Database.DatabaseClient` gRPC (`Services:DatabaseService`)
- **RPC clients:** gRPC clients (stubs from `Maichess.PlatformProtos`)

## Structure

```
MaichessAnalysisService/
  Domain/          # AnalysisGame, AnalysisSession, AnalysisResult, IAnalysisGameRepository, domain exceptions
  Data/            # AnalysisGameRepository (Database gRPC), AnalysisResultRepository (Database gRPC)
  Services/        # AnalysisGameService: PGN import, match import, FEN import, CRUD
                   # AnalysisSessionService: session lifecycle, engine streaming, socket push
  Rest/            # AnalysisEndpoints (thin HTTP adapters) + response DTOs
  Program.cs
```

## Key Design Decisions

### Game import

- **PGN import:** parse PGN → extract SAN moves. For each SAN move call
  `Moves.ValidateMoveSan(fen, san, position_history)` → `{ resulting_fen, position_history, uci_move }`.
  No local SAN parsing or chess logic. The PGN stored in the database uses the original PGN text.
- **Match import:** call `Database.Get(collection="matches", id=match_id)` directly — do NOT call
  any Match Manager gRPC endpoint. Read `status`, `white_user_id`, `black_user_id`, `white_bot_id`,
  `black_bot_id`, `moves`, `fen_history` fields. `fen_history[0]` = starting position;
  `fen_history[N]` = FEN after move N. For PGN generation, call
  `Moves.ConvertSequenceToSan(fen_history[0], match.moves)` to get all SAN moves in one call.
  The `position_history` field in the match document is ignored entirely.
- **FEN import:** `starting_fen = provided FEN`, `moves = []`, `fens = []`, `source = "fen"`.
- **Starting FEN:** stored in every `AnalysisGame`. Default is the standard opening position unless
  the PGN contains a `[FEN "..."]` header.

### Sessions

- **In-memory only.** Sessions are not persisted to the database. Server restart loses all sessions.
- **One per user.** Creating a new session auto-cancels and removes the previous one.
- **State fields:** `session_id`, `game_id`, `bot_id`, `line_count`, `current_index`,
  `whatif_moves` (List<string> UCI), `whatif_fens` (List<string>), `active_cts` (CancellationTokenSource?).
- **Current FEN:**
  - No whatif: `game.StartingFen` if `current_index == 0`, else `game.Fens[current_index - 1]`
  - Whatif active: `whatif_fens[last]`
- **Whatif validation:** `Moves.ValidateMove(current_fen, uci_move, empty_position_history)`.
  Position history starts empty for whatif branches — see Known Limitations.
- **Navigation** clears the whatif branch and cancels+restarts analysis.
- **Whatif move** cancels+restarts analysis at the new position.
- Analysis does NOT start automatically on session creation.

### Analysis engine stream

1. Query `analysis_results` for cached depths: `filter = { fen: currentFen, bot_id }`, post-filter
   `line_count >= session.line_count`, sort by `depth`, limit 100.
2. Emit all cached depths immediately via `Socket.EmitEvent` (event `"analysis_update"`), trimming
   lines to `session.line_count`.
3. Open `Engine.AnalyzePosition` stream.
4. For each engine update: if `depth <= max_cached_depth`, discard; else emit via socket AND
   (if `bot_id == DefaultAnalysisBotId && line_count == DefaultLineCount`) insert into
   `analysis_results`.
5. On stream end: emit `"analysis_complete"`.
6. On cancellation: exit silently.

All socket payloads include `session_id` so clients can correlate events.

### Startup bot-mismatch check

On application startup (before serving requests):
1. Read `DefaultAnalysisBotId` from config.
2. Query `analysis_meta` for document `id = "config"`. If absent, insert `{ id: "config", stored_bot_id: DefaultAnalysisBotId }` and exit.
3. If `stored_bot_id != DefaultAnalysisBotId`: call `Database.DeleteWhere(collection="analysis_results", filter={})` to scrape all cached data, then update `analysis_meta` with the new `stored_bot_id`.

### Whatif PGN export

For `GET /sessions/{id}/whatif/pgn`:
1. Get `whatif_base_fen` (the game position at `current_index`, before any whatif moves).
2. Call `Moves.ConvertSequenceToSan(whatif_base_fen, session.WhatifMoves)` → `san_moves[]`.
3. Build PGN with `[FEN "whatif_base_fen"]`, `[SetUp "1"]` headers and `san_moves` as the move list.

## Known Limitations

- **Whatif threefold repetition**: position history for whatif branches starts empty; repetition
  detection only covers positions within the branch, not the preceding game moves.

## Code Style

- All compiler warnings are errors; `CS1591` exempted.
- `EnableNETAnalyzers`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`, StyleCop.Analyzers.
- Sealed classes throughout; no public types unless required by framework.
- C# records for DTOs and response models.
- Validate at REST boundaries; trust internal data after that point.
- No comments unless explaining a non-obvious constraint or algorithm.

## Testing Requirements

- 100% coverage (line, branch, method) on all non-excluded code. Mandatory.
- Test framework: Reqnroll BDD (feature files + step definitions) for `AnalysisGameService` and
  `AnalysisSessionService` business logic; plain xUnit `[Fact]` for all other testable units.
- Write tests alongside every code change.
- Excluded from coverage (`[ExcludeFromCodeCoverage]`):
  - `AnalysisEndpoints` (REST adapter)
  - `AnalysisGameRepository`, `AnalysisResultRepository` (require live gRPC/DB)
  - Compiler-generated logging partials (`[LoggerMessage]` methods)
  - All REST DTO record types
- Coverlet: exclude `Program.cs`, `*.g.cs`, `*.generated.cs`.
- Run `dotnet test -p:CollectCoverage=true` before marking any task complete.

### Mutation testing

Stryker.NET is wired up as a local dotnet tool. Config lives in
`MaichessAnalysisService.Tests/stryker-config.json`; the same files excluded
from coverage are also excluded from mutation. Run via `dotnet tool restore`
then `dotnet stryker` inside the test project directory. See `README.md` for
details. Mutation testing is not required to pass on every change, but use it
when investigating whether tests genuinely exercise behaviour.

## Environment Variables

| Config key | Description |
|---|---|
| `Services:DatabaseService` | match-db Database Service gRPC address |
| `Services:EngineService` | Engine Service gRPC address |
| `Services:MoveValidatorService` | Move Validator gRPC address |
| `Services:SocketService` | Socket Service gRPC address |
| `Jwt:Key` | JWT signing key (same value as other services) |
| `Analysis:DefaultBotId` | Bot ID whose analysis results are cached. Mismatch triggers cache scrape on startup. |
| `Analysis:DefaultLineCount` | Line count used for caching (analysis results are only written when `line_count` matches this value) |
