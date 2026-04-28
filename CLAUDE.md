# Analysis Service

Manages saved analysis games and relays engine analysis streams.

## Contracts

- **REST (server):** `maichess-api-contracts/rest/analysis.md`
- **gRPC (server):** `maichess-api-contracts/protos/analysis-service/v1/analysis.proto` — `StreamPositionAnalysis`
- **gRPC (clients):**
  - `protos/database-service/v1/database.proto` — `Get`, `List`, `Insert`, `Delete`
  - `protos/engine-service/v1/bots.proto` — `AnalyzePosition`
  - `protos/match-manager-service/v1/matches.proto` — `GetMatch`, `GetMatchPosition`
- **Generated stubs:** `Maichess.PlatformProtos` NuGet (see `maichess-api-contracts/dotnet/`)

Implement against these contracts exactly. Document any blocker in `CONTRACT_NOTES.md`.

## Stack

- **Runtime:** ASP.NET (net10.0), C#, nullable enabled
- **Database:** match-db (MongoDB) via `Database.DatabaseClient` gRPC (`Services:DatabaseService`)
- **RPC:** gRPC server + gRPC clients (stubs from `Maichess.PlatformProtos`)
- **PGN parsing:** custom lightweight parser (see Services layer notes)

## Structure

```
MaichessAnalysisService/
  Domain/          # AnalysisGame entity, IAnalysisGameRepository, domain exceptions
  Data/            # AnalysisGameRepository (Database gRPC client)
  Services/        # AnalysisGameService: PGN import, from-match import, CRUD
  Grpc/            # AnalysisGrpcService: StreamPositionAnalysis relay to Engine
  Rest/            # AnalysisEndpoints (thin HTTP adapters) + response DTOs
  Program.cs
```

## Key Design Decisions

- **FEN derivation:** Computed once on import and stored; never recomputed on read.
- **User scoping:** All `List` queries include `user_id` equality filter. Cross-user access is
  rejected in the service layer before any DB call.
- **from-match access:** Fetch the match via `Matches.GetMatch`. Verify `match.Status != Ongoing`.
  Verify the requesting user is White or Black. On failure throw the appropriate domain exception.
- **FEN history from match:** `GetMatch` returns the current FEN only, not the full history.
  Reconstruct by calling `GetMatchPosition` for indices 1..moves.Count. This is N gRPC calls;
  document the blocker in `CONTRACT_NOTES.md` and propose adding `fen_history` to the `Match` proto.
- **PGN parsing:** Parse PGN using a simple hand-written parser (no external library needed):
  extract bracket-delimited tags into a dictionary, then parse the movetext by stripping move
  numbers, result tokens, comments `{...}`, and annotations `$N`. The remaining tokens are SAN
  moves. To generate UCI moves and FENs, call `Matches.GetMatchPosition` is NOT available here
  (no match context). Instead, use the Move Validator via `Moves.GetLegalMoves` to convert each
  SAN move to UCI: given a FEN, call `GetLegalMoves` to get all legal UCI moves, then find the
  one that matches the SAN move by applying standard SAN disambiguation rules. Track the current
  FEN after each move using the result from `ValidateMove`. This requires both `Moves.MovesClient`
  and `Moves.GetLegalMoves` / `ValidateMove` RPCs.
- **StreamPositionAnalysis:** Pure transparent relay — open `Bots.AnalyzePosition` with the
  request parameters and pipe each `AnalysisUpdate` → `PositionAnalysisUpdate` (mapping
  `PrincipalVariation` fields to `AnalysisLine` fields 1:1). Propagate caller cancellation.

## Code Style

- All compiler warnings are errors; `CS1591` exempted.
- `EnableNETAnalyzers`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`, StyleCop.Analyzers.
- Sealed classes throughout; no public types unless required by framework.
- C# records for DTOs and response models.
- Validate at REST/gRPC boundaries; trust internal data after that point.
- No comments unless explaining a non-obvious constraint or algorithm.

## Testing Requirements

- 100% coverage (line, branch, method) on all non-excluded code. Mandatory.
- Test framework: Reqnroll BDD (feature files + step definitions) for `AnalysisGameService`
  business logic; plain xUnit `[Fact]` for `AnalysisGrpcService`.
- Write tests alongside every code change.
- Excluded from coverage (`[ExcludeFromCodeCoverage]`):
  - `AnalysisEndpoints` (REST adapter)
  - `AnalysisGameRepository` (requires live gRPC/DB)
  - Compiler-generated logging partials (`[LoggerMessage]` methods)
  - All REST DTO record types
- Coverlet: exclude `Program.cs`, `*.g.cs`, `*.generated.cs`.
- Run `dotnet test -p:CollectCoverage=true` before marking any task complete.

## Environment Variables

| Config key (env double-underscore form) | Description |
|---|---|
| `Services:DatabaseService` | match-db Database Service gRPC address |
| `Services:EngineService` | Engine Service gRPC address |
| `Services:MatchManagerService` | Match Manager gRPC address |
| `Services:MoveValidatorService` | Move Validator gRPC address (for PGN SAN→UCI) |
| `Jwt:Key` | JWT signing key (same value as other services) |
