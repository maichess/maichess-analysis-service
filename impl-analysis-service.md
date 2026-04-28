# Implementation Prompt: maichess-analysis-service

You are implementing three things in one session:

1. **Analysis Service** — a new ASP.NET (net10.0) microservice (`services/maichess-analysis-service/`)
2. **Analysis WebSocket Service** — a new Node.js/TypeScript service (`services/maichess-analysis-ws-service/`)
3. **Client integration** — new pages, API proxy routes, and hooks in `maichess-client/`

Work through them in order. Do not start a later step until the previous one compiles and its
tests pass.

---

## Read These Files First

Before writing any code, read every file in this list. They define the contracts, patterns, and
constraints you must follow exactly.

### Contracts
- `maichess-api-contracts/protos/analysis-service/v1/analysis.proto`
- `maichess-api-contracts/protos/database-service/v1/database.proto`
- `maichess-api-contracts/protos/engine-service/v1/bots.proto`
- `maichess-api-contracts/protos/match-manager-service/v1/matches.proto`
- `maichess-api-contracts/protos/auth-service/v1/auth.proto`
- `maichess-api-contracts/rest/analysis.md`
- `maichess-api-contracts/grpc-overview.md`
- `maichess-knowledge-base/analysis-service.md`
- `maichess-knowledge-base/maichess-structure.md`

### ASP.NET patterns (follow these exactly)
- `services/maichess-match-manager-service/MaichessMatchManagerService/Program.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Data/IMatchRepository.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Data/MatchRepository.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Services/MatchService.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Grpc/MatchesGrpcService.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Rest/MatchesEndpoints.cs`
- `services/maichess-match-manager-service/MaichessMatchManagerService/Rest/MatchResponse.cs`
- `services/maichess-match-manager-service/CLAUDE.md`
- `services/maichess-match-manager-service/MaichessMatchManagerService/MaichessMatchManagerService.csproj`
- `services/maichess-database-service/nuget.config`

### Node.js patterns (follow these exactly)
- `services/maichess-socket-service/src/index.ts`
- `services/maichess-socket-service/src/grpc/server.ts`
- `services/maichess-socket-service/src/grpc/auth-client.ts`
- `services/maichess-socket-service/CLAUDE.md`
- `services/maichess-socket-service/package.json`
- `services/maichess-socket-service/tsconfig.json`
- `services/maichess-socket-service/.npmrc`

### Client patterns (follow these exactly)
- `maichess-client/CLAUDE.md`
- `maichess-client/lib/models/match.ts`
- `maichess-client/lib/hooks/useMatchEvents.ts`
- `maichess-client/lib/hooks/useSocket.ts`
- `maichess-client/lib/hooks/useMatch.ts`
- `maichess-client/lib/components/MatchClient.tsx`
- `maichess-client/lib/components/ChessBoard.tsx`
- `maichess-client/lib/components/MoveList.tsx`
- `maichess-client/lib/components/GameStatus.tsx`
- `maichess-client/app/match/[id]/page.tsx`
- `maichess-client/lib/constants/routes.ts`

---

## Step 1: Analysis Service

### 1a. Project setup

Replace `services/maichess-analysis-service/maichess-analysis-service.csproj` entirely:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>MaichessAnalysisService</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- PublishAot removed: gRPC is incompatible with NativeAOT -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>All</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>MaichessAnalysisService.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>DynamicProxyGenAssembly2</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="DotNetEnv" Version="3.1.1" />
    <PackageReference Include="Grpc.AspNetCore" Version="2.76.0" />
    <PackageReference Include="Maichess.PlatformProtos" Version="0.2.12" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.1" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.1" />
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="All" />
  </ItemGroup>

</Project>
```

Create `services/maichess-analysis-service/nuget.config` — copy from
`services/maichess-database-service/nuget.config` verbatim.

### 1b. CLAUDE.md

Create `services/maichess-analysis-service/CLAUDE.md`:

```markdown
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
  **Simpler alternative (recommended):** Add Move Validator as a gRPC dependency and call
  `ValidateMove` for each move in sequence — pass the current FEN plus the UCI move and receive
  the resulting FEN. The SAN→UCI conversion can be done client-side using the legal moves list.
  Document the approach chosen in `CONTRACT_NOTES.md`.
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
```

### 1c. Domain layer (`Domain/`)

**`AnalysisGame.cs`**:
```csharp
internal sealed record AnalysisGame(
    string Id,
    string UserId,
    string Source,
    string? MatchId,
    IReadOnlyList<string> Moves,
    IReadOnlyList<string> Fens,
    string Pgn,
    string Result,
    IReadOnlyDictionary<string, string> White,
    IReadOnlyDictionary<string, string> Black,
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset CreatedAt);
```

`White`, `Black`, and `Tags` are flat string→string maps. Player maps contain different keys
depending on the source:
- PGN import: `{ "name": "Fischer" }`
- Match import (human): `{ "user_id": "...", "username": "..." }` — username may be omitted if
  resolution is not feasible during import
- Match import (bot): `{ "bot_id": "...", "name": "..." }`

**`IAnalysisGameRepository.cs`**:
```csharp
internal interface IAnalysisGameRepository
{
    Task<AnalysisGame?> GetByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<AnalysisGame>> ListByUserIdAsync(
        string userId, int limit, int offset, CancellationToken ct);
    Task<int> CountByUserIdAsync(string userId, CancellationToken ct);
    Task<AnalysisGame> InsertAsync(AnalysisGame game, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}
```

**Domain exceptions** (each a sealed class inheriting `Exception`):
- `AnalysisGameNotFoundException`
- `AccessDeniedException`
- `MatchStillOngoingException`
- `MatchAccessDeniedException`
- `InvalidPgnException(string Reason)` — exposes `Reason` property

### 1d. Data layer (`Data/`)

**`AnalysisGameRepository.cs`** — `[ExcludeFromCodeCoverage]`, injects `Database.DatabaseClient`:

Collection: `"analysis_games"`

Struct field mapping (follow `MatchRepository.cs` style exactly — explicit `Struct.Fields[key] =
Value.For*(...)` assignments, no helper libraries):
- Scalar strings: `Value.ForString(...)`
- Nullable string: `Value.ForString(x) or Value.ForNull()`
- Lists (moves, fens): `Value.ForList(items.Select(Value.ForString).ToArray())`
- Sub-maps (white, black, tags): build an inner `Struct` from the dictionary entries, then wrap
  with `Value.ForStruct(innerStruct)`
- DateTimeOffset: `Value.ForString(dt.ToString("O", CultureInfo.InvariantCulture))`

When reading back from a Struct:
- For inner maps, iterate `Fields` of the nested struct and cast `Value.KindCase == StringValue`.
- For lists, use `.ListValue.Values.Select(v => v.StringValue).ToList()`.
- Parse `created_at` with `DateTimeOffset.Parse(s, CultureInfo.InvariantCulture)`.

`CountByUserIdAsync`: use `db.ListAsync` with the user_id filter, limit=10000, offset=0, return
`records.Count`. This approximates the count for pagination. Document as a blocker in
`CONTRACT_NOTES.md` (see §1h).

`DeleteAsync`: catch `RpcException` with `StatusCode.NotFound` and rethrow as
`AnalysisGameNotFoundException`.

### 1e. Services layer (`Services/`)

**`AnalysisGameService.cs`** — injected with `IAnalysisGameRepository`,
`Matches.MatchesClient`, `Moves.MovesClient`.

#### `GetGameAsync(string id, string userId, CancellationToken ct)`
1. `GetByIdAsync(id, ct)` → null → `AnalysisGameNotFoundException`
2. `game.UserId != userId` → `AccessDeniedException`
3. Return game.

#### `ListGamesAsync(string userId, int page, int pageSize, CancellationToken ct)`
1. Clamp `pageSize` to [1, 100]; clamp `page` to ≥ 1.
2. `offset = (page - 1) * pageSize`
3. Parallel: `CountByUserIdAsync` + `ListByUserIdAsync(userId, pageSize, offset, ct)`
4. Return `(games, total, page, pageSize)`.

#### `DeleteGameAsync(string id, string userId, CancellationToken ct)`
1. `GetByIdAsync` → null → `AnalysisGameNotFoundException`
2. `game.UserId != userId` → `AccessDeniedException`
3. `DeleteAsync(id, ct)`.

#### `ImportFromPgnAsync(string pgn, string userId, CancellationToken ct)`

PGN parsing (implement without a chess library):

**Phase 1 — tag extraction:**
Parse all `[Key "Value"]` headers into a `Dictionary<string, string>`. Common tags to look for:
`White`, `Black`, `Result`, `Event`, `Site`, `Date`, and any others present.

**Phase 2 — movetext parsing:**
Extract everything after the last `]` tag block. Strip:
- Move numbers (`1.`, `2.`, `12.`, `1...` etc.) — regex `\d+\.+`
- Comments `{...}` (including nested braces is not required — single-level is sufficient)
- Annotations `$\d+`
- Result tokens (`1-0`, `0-1`, `1/2-1/2`, `*`)
Split remaining tokens on whitespace. Each token is a SAN move (e.g. `e4`, `Nf3`, `O-O`).

If the movetext is empty, the game has zero moves — that is valid (store with empty moves/fens).
If the PGN string has no tag block and no moves, throw `InvalidPgnException("empty pgn")`.

**Phase 3 — SAN → UCI + FEN generation:**
Starting from the standard opening FEN
`"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"`:
For each SAN move:
1. Call `movesClient.GetLegalMovesAsync(new GetLegalMovesRequest { Fen = currentFen })` to get
   all legal UCI moves.
2. Find the UCI move that corresponds to the SAN move using `MatchSanToUci(san, legalUciMoves,
   currentFen)` — a helper that maps SAN notation to UCI (see implementation notes below).
3. If no match found, throw `InvalidPgnException($"illegal or unrecognised move: {san}")`.
4. Call `movesClient.ValidateMoveAsync(new ValidateMoveRequest { Fen = currentFen, Move = uci,
   PositionHistory = { currentPositionHistory } })` to get the resulting FEN.
5. Append `uci` to the moves list and the resulting FEN to the fens list.
6. Update `currentFen` to `response.ResultingFen`.

`MatchSanToUci` rules:
- Castling: `O-O` → any UCI move of the form `e1g1` or `e8g8`; `O-O-O` → `e1c1` or `e8c8`.
- Pawn moves: `e4` → UCI moves ending in `e4` from a pawn on the same file; `exd5` → UCI `e_d5`
  where `_` is any digit.
- Piece moves: `Nf3` → UCI moves ending in `f3` from a knight. Use the FEN to identify pieces.
- Promotions: `e8=Q` → UCI `e7e8q`.
- Disambiguation (e.g. `Rad1`, `R1d3`): apply standard SAN disambiguation — filter candidates
  by file letter or rank digit before the destination square.
- Check/mate suffixes (`+`, `#`): strip before matching.

This helper is pure logic on strings; it is fully unit-testable.

**Phase 4 — build and store:**
```csharp
var game = new AnalysisGame(
    Id: string.Empty,   // assigned by repository
    UserId: userId,
    Source: "pgn",
    MatchId: null,
    Moves: uciMoves,
    Fens: fens,
    Pgn: pgn.Trim(),
    Result: tags.GetValueOrDefault("Result", "*"),
    White: new Dictionary<string, string> { ["name"] = tags.GetValueOrDefault("White", "?") },
    Black: new Dictionary<string, string> { ["name"] = tags.GetValueOrDefault("Black", "?") },
    Tags: tags,
    CreatedAt: DateTimeOffset.UtcNow);
return await repo.InsertAsync(game, ct);
```

#### `ImportFromMatchAsync(string matchId, string userId, CancellationToken ct)`

1. `matchesClient.GetMatchAsync(new GetMatchRequest { MatchId = matchId }, ct)`:
   - Let `RpcException(NotFound)` propagate (endpoint maps it to 404).
2. Match `match.Status` — if `MatchStatus.Ongoing` → `MatchStillOngoingException`.
3. Participant check: extract the user ID from White and Black players (`UserId` on the `oneof`).
   If neither equals `userId` → `MatchAccessDeniedException`.
4. Reconstruct FEN history: call `matchesClient.GetMatchPositionAsync` for indices 1 to
   `match.Moves.Count`. Index 0 is always the standard starting FEN. Collect the `Fen` field from
   each response into the `fens` list (length equals `match.Moves.Count`).
5. Build player info maps:
   - `IdentityCase.UserId` → `{ "user_id": player.UserId }` (username not resolved here)
   - `IdentityCase.BotId` → `{ "bot_id": player.BotId }`
6. Map match status to PGN result token:
   - `WhiteWon` → `"1-0"`, `BlackWon` → `"0-1"`, `Draw` → `"1/2-1/2"`, `Ongoing` → `"*"`
7. Build and insert:
```csharp
var game = new AnalysisGame(
    Id: string.Empty,
    UserId: userId,
    Source: "match",
    MatchId: matchId,
    Moves: [.. match.Moves],
    Fens: fens,
    Pgn: BuildMinimalPgn(match, whiteInfo, blackInfo, result),
    Result: result,
    White: whiteInfo,
    Black: blackInfo,
    Tags: BuildMatchTags(match, result),
    CreatedAt: DateTimeOffset.UtcNow);
return await repo.InsertAsync(game, ct);
```

`BuildMinimalPgn` generates a PGN string with standard headers and moves in UCI notation (SAN is
not required for the stored PGN; document this simplification).

### 1f. gRPC layer (`Grpc/`)

**`AnalysisGrpcService.cs`** — pure relay, injects `Bots.BotsClient`:

```csharp
internal sealed class AnalysisGrpcService(Bots.BotsClient botsClient)
    : Analysis.AnalysisBase
{
    public override async Task StreamPositionAnalysis(
        StreamPositionAnalysisRequest request,
        IServerStreamWriter<PositionAnalysisUpdate> responseStream,
        ServerCallContext context)
    {
        using AsyncServerStreamingCall<AnalysisUpdate> engineCall = botsClient.AnalyzePosition(
            new AnalyzePositionRequest
            {
                Fen = request.Fen,
                BotId = request.BotId,
                LineCount = request.LineCount,
            },
            cancellationToken: context.CancellationToken);

        await foreach (AnalysisUpdate update in
            engineCall.ResponseStream.ReadAllAsync(context.CancellationToken))
        {
            PositionAnalysisUpdate relayed = new() { Depth = update.Depth };
            relayed.Lines.AddRange(update.Lines.Select(pv => new AnalysisLine
            {
                Rank = pv.Rank,
                EvaluationCp = pv.EvaluationCp,
                Moves = { pv.Moves },
            }));
            await responseStream.WriteAsync(relayed, context.CancellationToken);
        }
    }
}
```

### 1g. REST layer (`Rest/`)

**`AnalysisEndpoints.cs`** — `[ExcludeFromCodeCoverage]`, static extension class.
Follow `MatchesEndpoints.cs` style exactly: one private `static async Task<IResult>` method per
endpoint, all injected via parameter binding (DI).

Routes under `/games`, all requiring `[Authorize]`:

| Method | Path | Handler | Success | Errors |
|---|---|---|---|---|
| GET | `/games` | `ListGames` | 200 | 401 |
| GET | `/games/{id}` | `GetGame` | 200 | 401, 403, 404 |
| POST | `/games` | `ImportPgn` | 201 | 400, 401 |
| POST | `/games/from-match/{matchId}` | `ImportFromMatch` | 201 | 400, 401, 403, 404 |
| DELETE | `/games/{id}` | `DeleteGame` | 204 | 401, 403, 404 |

Exception → HTTP status mapping:
- `AnalysisGameNotFoundException` → 404
- `AccessDeniedException` → 403
- `MatchStillOngoingException` → 400 `{ "error": "match is still ongoing" }`
- `MatchAccessDeniedException` → 403
- `InvalidPgnException ex` → 400 `{ "error": ex.Reason }`
- `RpcException` with `StatusCode.NotFound` (propagated from GetMatch) → 404

**Response DTO records** — all `[ExcludeFromCodeCoverage]`, sealed, with `[JsonPropertyName]`:

`GameSummaryResponse`: id, source, match_id (nullable), white, black, result, move_count,
created_at (ISO string), tags.

`GameDetailResponse`: extends summary fields, adds moves (string[]), fens (string[]), pgn.

`GamesListResponse`: games (GameSummaryResponse[]), total, page, page_size.

For `white` / `black`, serialize the `IReadOnlyDictionary<string, string>` directly — the
JSON output will be the object with whatever keys are present (`name`, `user_id`, `bot_id`, etc.).
Use `[JsonConverter(typeof(DictionaryStringStringConverter))]` if needed, or simply include them
as `object` typed properties.

`ImportPgnRequest`: `record ImportPgnRequest([property: JsonPropertyName("pgn")] string Pgn)`.

### 1h. `Program.cs`

```csharp
using System.Text;
using Grpc.Net.Client;
using Maichess.Analysis.V1;
using Maichess.Database.V1;
using Maichess.Engine.V1;
using Maichess.MatchManager.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Data;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Grpc;
using MaichessAnalysisService.Rest;
using MaichessAnalysisService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

DotNetEnv.Env.Load();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string dbUrl = builder.Configuration["Services:DatabaseService"]
    ?? throw new InvalidOperationException("Services:DatabaseService is not configured");
string engineUrl = builder.Configuration["Services:EngineService"]
    ?? throw new InvalidOperationException("Services:EngineService is not configured");
string matchManagerUrl = builder.Configuration["Services:MatchManagerService"]
    ?? throw new InvalidOperationException("Services:MatchManagerService is not configured");
string moveValidatorUrl = builder.Configuration["Services:MoveValidatorService"]
    ?? throw new InvalidOperationException("Services:MoveValidatorService is not configured");
string jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured");

builder.Services.AddSingleton(new Database.DatabaseClient(GrpcChannel.ForAddress(dbUrl)));
builder.Services.AddSingleton(new Bots.BotsClient(GrpcChannel.ForAddress(engineUrl)));
builder.Services.AddSingleton(new Matches.MatchesClient(GrpcChannel.ForAddress(matchManagerUrl)));
builder.Services.AddSingleton(new Moves.MovesClient(GrpcChannel.ForAddress(moveValidatorUrl)));

builder.Services.AddSingleton<IAnalysisGameRepository, AnalysisGameRepository>();
builder.Services.AddSingleton<AnalysisGameService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out string? token))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddGrpc();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.MapGrpcService<AnalysisGrpcService>();
app.MapAnalysisEndpoints();

app.Run();
```

### 1i. Tests (`MaichessAnalysisService.Tests/`)

Create a separate test project. Follow the match-manager test project structure exactly (Reqnroll
for service layer BDD, xUnit for gRPC and utility unit tests).

**Feature files (Reqnroll BDD) for `AnalysisGameService`:**

- `Features/ImportFromPgn.feature`:
  - Scenario: valid PGN with moves imports successfully
  - Scenario: PGN with empty movetext imports with zero moves
  - Scenario: PGN with missing White/Black tags defaults to `"?"`
  - Scenario: malformed PGN (no tags, no moves) throws InvalidPgnException
  - Scenario: PGN with an illegal move throws InvalidPgnException with the move in the reason

- `Features/ImportFromMatch.feature`:
  - Scenario: finished match imports successfully with correct FEN history
  - Scenario: ongoing match import throws MatchStillOngoingException
  - Scenario: import by non-participant throws MatchAccessDeniedException
  - Scenario: match not found propagates RpcException(NotFound)

- `Features/GetGame.feature`:
  - Scenario: owner retrieves their game
  - Scenario: game not found throws AnalysisGameNotFoundException
  - Scenario: non-owner throws AccessDeniedException

- `Features/ListGames.feature`:
  - Scenario: first page returns correct slice
  - Scenario: page beyond total returns empty games list
  - Scenario: page_size is clamped to 100

- `Features/DeleteGame.feature`:
  - Scenario: owner deletes their game
  - Scenario: game not found throws AnalysisGameNotFoundException
  - Scenario: non-owner throws AccessDeniedException

**Unit tests (xUnit) for `AnalysisGrpcService`:**
- Mock `Bots.BotsClient`. Verify that each `AnalysisUpdate` from the engine is relayed as a
  `PositionAnalysisUpdate` with correct field mapping (`depth`, `rank`, `evaluation_cp`, `moves`).
- Verify that cancellation on the server context propagates and the engine stream is cancelled.

**Unit tests for `MatchSanToUci` helper** (pure logic, no mocks):
- Pawn push, pawn capture, castling kingside, castling queenside, promotion, piece move,
  disambiguation by file, disambiguation by rank.

Coverage: run `dotnet test -p:CollectCoverage=true` and confirm 100% on non-excluded code.

### 1j. `CONTRACT_NOTES.md`

Create at `services/maichess-analysis-service/CONTRACT_NOTES.md`:

```markdown
# Contract Notes — Analysis Service

## Database.Count RPC missing

The Database Service proto (`database.proto`) has no `Count` RPC. `GET /games` requires a total
count for pagination metadata.

Workaround: `CountByUserIdAsync` calls `Database.List` with `limit=10000` and returns
`records.Count`. This is O(N) and degrades for users with many saved games.

Proposed change: add to `database.proto`:
```proto
rpc Count(CountRequest) returns (CountResponse);
message CountRequest { string collection = 1; google.protobuf.Struct filter = 2; }
message CountResponse { int64 count = 1; }
```
Do not implement until explicitly told to proceed.

## Match.fen_history missing from GetMatch response

`Matches.GetMatch` returns a `Match` message that contains `current_fen` and `moves` but no
full FEN history. `ImportFromMatchAsync` must call `Matches.GetMatchPosition` for every move
index to reconstruct the history — N gRPC round-trips for an N-move game.

Proposed change: add `repeated string fen_history = 10;` to the `Match` message in
`matches.proto` and populate it in `GetMatch`.

Do not implement until explicitly told to proceed.
```

---

## Step 2: Analysis WebSocket Service

Create a new directory `services/maichess-analysis-ws-service/`. Follow the socket-service
(`services/maichess-socket-service/`) patterns exactly. Read its full source before starting.

This service bridges WebSocket JSON messages (from the client) to the Analysis gRPC streaming
endpoint. It is a plain WebSocket server (using the `ws` npm package), NOT socket.io.

### 2a. Project setup

```bash
cd services/maichess-analysis-ws-service
npm init -y
npm install express ws @grpc/grpc-js @maichess/platform-protos dotenv
npm install --save-dev typescript @types/node @types/express @types/ws nodemon ts-node
```

Copy `.npmrc` from `services/maichess-socket-service/.npmrc` verbatim (GitHub Packages auth for
`@maichess/platform-protos`).

`tsconfig.json` — copy from socket-service.

`package.json` scripts:
```json
{
  "dev": "nodemon --exec ts-node src/index.ts",
  "build": "tsc"
}
```

### 2b. Directory structure

```
src/
  index.ts                   # Bootstrap: env checks, HTTP server, WS server
  ws/
    server.ts                # WebSocket server setup and per-connection lifecycle
    types.ts                 # Client and server message type definitions
  grpc/
    analysis-client.ts       # gRPC client for Analysis.StreamPositionAnalysis
    auth-client.ts           # gRPC client for Auth.ValidateToken
  middleware/
    error.ts                 # Express error middleware
```

### 2c. `src/ws/types.ts`

```typescript
export type ClientMessage =
  | { type: 'start_analysis'; fen: string; bot_id: string; line_count: number }
  | { type: 'stop_analysis' }

export type ServerMessage =
  | { type: 'analysis_update'; depth: number; lines: AnalysisLine[] }
  | { type: 'analysis_complete'; final_depth: number }
  | { type: 'error'; message: string }

export interface AnalysisLine {
  rank: number
  evaluation_cp: number
  moves: string[]
}
```

### 2d. `src/grpc/auth-client.ts`

Copy `services/maichess-socket-service/src/grpc/auth-client.ts` verbatim. Same `Auth.ValidateToken`
call, same module-level singleton pattern.

### 2e. `src/grpc/analysis-client.ts`

```typescript
import * as grpc from '@grpc/grpc-js'
import {
  AnalysisClient,
  type StreamPositionAnalysisRequest,
  type PositionAnalysisUpdate,
} from '@maichess/platform-protos/analysis-service/v1/analysis'

let client: AnalysisClient | null = null

function getClient(): AnalysisClient {
  if (!client) {
    const addr = process.env.ANALYSIS_SERVICE_GRPC_ADDR
    if (!addr) throw new Error('ANALYSIS_SERVICE_GRPC_ADDR is required')
    client = new AnalysisClient(addr, grpc.credentials.createInsecure())
  }
  return client
}

export function streamAnalysis(
  request: StreamPositionAnalysisRequest,
  signal: AbortSignal
): grpc.ClientReadableStream<PositionAnalysisUpdate> {
  const call = getClient().streamPositionAnalysis(request)
  signal.addEventListener('abort', () => call.cancel())
  return call
}
```

### 2f. `src/ws/server.ts`

```typescript
import { WebSocketServer, WebSocket } from 'ws'
import type { Server as HttpServer } from 'http'
import type { IncomingMessage } from 'http'
import { validateToken } from '../grpc/auth-client'
import { streamAnalysis } from '../grpc/analysis-client'
import type { ClientMessage, ServerMessage, AnalysisLine } from './types'

export function createWsServer(httpServer: HttpServer): WebSocketServer {
  const wss = new WebSocketServer({ server: httpServer, path: '/analysis' })

  wss.on('connection', async (ws: WebSocket, req: IncomingMessage) => {
    const url = new URL(req.url ?? '', `http://${req.headers.host}`)
    const token = url.searchParams.get('token')

    if (!token) {
      send(ws, { type: 'error', message: 'missing token' })
      ws.close(1008, 'unauthorized')
      return
    }

    try {
      const result = await validateToken(token)
      if (!result.valid) throw new Error('invalid')
    } catch {
      send(ws, { type: 'error', message: 'unauthorized' })
      ws.close(1008, 'unauthorized')
      return
    }

    let abortController: AbortController | null = null

    ws.on('message', (raw) => {
      let msg: ClientMessage
      try {
        msg = JSON.parse(raw.toString()) as ClientMessage
      } catch {
        send(ws, { type: 'error', message: 'invalid message format' })
        return
      }

      if (msg.type === 'stop_analysis') {
        abortController?.abort()
        abortController = null
        return
      }

      if (msg.type === 'start_analysis') {
        abortController?.abort()
        abortController = new AbortController()
        const { signal } = abortController

        const stream = streamAnalysis(
          { fen: msg.fen, botId: msg.bot_id, lineCount: msg.line_count },
          signal
        )

        let lastDepth = 0

        stream.on('data', (update: { depth: number; lines: Array<{ rank: number; evaluationCp: number; moves: string[] }> }) => {
          lastDepth = update.depth
          const lines: AnalysisLine[] = update.lines.map(l => ({
            rank: l.rank,
            evaluation_cp: l.evaluationCp,
            moves: l.moves,
          }))
          send(ws, { type: 'analysis_update', depth: update.depth, lines })
        })

        stream.on('end', () => {
          send(ws, { type: 'analysis_complete', final_depth: lastDepth })
          abortController = null
        })

        stream.on('error', (err: Error & { code?: number }) => {
          if (err.code === 1) return  // CANCELLED — client aborted, silent
          send(ws, { type: 'error', message: 'engine unavailable' })
          abortController = null
        })
      }
    })

    ws.on('close', () => {
      abortController?.abort()
    })
  })

  return wss
}

function send(ws: WebSocket, msg: ServerMessage): void {
  if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(msg))
}
```

### 2g. `src/index.ts`

```typescript
import 'dotenv/config'
import http from 'http'
import express from 'express'
import { errorMiddleware } from './middleware/error'
import { createWsServer } from './ws/server'

for (const envVar of ['AUTH_SERVICE_GRPC_ADDR', 'ANALYSIS_SERVICE_GRPC_ADDR']) {
  if (!process.env[envVar]) throw new Error(`${envVar} environment variable is required`)
}

const app = express()
app.use(errorMiddleware)

const httpServer = http.createServer(app)
createWsServer(httpServer)

const PORT = process.env.PORT ?? '3001'
httpServer.listen(Number(PORT), () => {
  console.log(`Analysis WebSocket service listening on port ${PORT}`)
})
```

### 2h. `src/middleware/error.ts`

Copy from `services/maichess-socket-service/src/middleware/error.ts` verbatim.

### 2i. `CLAUDE.md`

Create `services/maichess-analysis-ws-service/CLAUDE.md` following socket-service's CLAUDE.md
style:

```markdown
# CLAUDE.md — maichess-analysis-ws-service

## Role

WebSocket bridge service. Accepts persistent WebSocket connections from the client on
`/analysis`, authenticates via JWT query parameter, and relays analysis streaming to/from the
Analysis Service gRPC endpoint.

## Contracts

- **gRPC (client):** `maichess-api-contracts/protos/analysis-service/v1/analysis.proto` — `StreamPositionAnalysis`
- **gRPC (client):** `maichess-api-contracts/protos/auth-service/v1/auth.proto` — `ValidateToken`
- **WebSocket protocol:** defined in `maichess-knowledge-base/analysis-service.md` (WebSocket Analysis Streaming section)

## Stack

- **Runtime:** Node.js, TypeScript
- **WebSocket:** `ws` (plain WebSocket — NOT socket.io)
- **Framework:** Express.js (HTTP server only; no routes used beyond health checks)
- **gRPC:** clients only

## Endpoint

`ws://<host>/analysis?token=<jwt>`

## Architecture

```
src/
  ws/server.ts          # WebSocket server and per-connection stream lifecycle
  ws/types.ts           # Message type definitions
  grpc/analysis-client.ts  # Analysis.StreamPositionAnalysis client
  grpc/auth-client.ts   # Auth.ValidateToken client
  middleware/error.ts   # Express error handler
  index.ts              # Bootstrap
```

## Coding Principles

Same as socket-service: no classes for stateless logic, one responsibility per module,
explicit over implicit, fail fast on bad auth.

## Environment Variables

| Variable | Description |
|---|---|
| `PORT` | HTTP/WebSocket port (default `3001`) |
| `AUTH_SERVICE_GRPC_ADDR` | Auth service gRPC address (required) |
| `ANALYSIS_SERVICE_GRPC_ADDR` | Analysis service gRPC address (required) |
```

---

## Step 3: Client Integration

Read `maichess-client/CLAUDE.md` before writing any client code. This is Next.js 16 App Router.
Use server components wherever possible. Follow existing patterns for hooks and components.

### 3a. `lib/models/analysis.ts` (new file)

```typescript
export type PlayerInfo =
  | { user_id: string; username?: string }
  | { bot_id: string; name: string }
  | { name: string }

export interface AnalysisGameSummary {
  id: string
  source: 'pgn' | 'match'
  match_id?: string
  white: PlayerInfo
  black: PlayerInfo
  result: string
  move_count: number
  created_at: string
  tags: Record<string, string>
}

export interface AnalysisGame extends AnalysisGameSummary {
  moves: string[]
  fens: string[]
  pgn: string
}

export interface AnalysisLine {
  rank: number
  evaluation_cp: number
  moves: string[]
}

export type AnalysisServerMessage =
  | { type: 'analysis_update'; depth: number; lines: AnalysisLine[] }
  | { type: 'analysis_complete'; final_depth: number }
  | { type: 'error'; message: string }

export function playerInfoDisplayName(p: PlayerInfo): string {
  if ('user_id' in p) return p.username ?? p.user_id
  if ('bot_id' in p) return p.name
  return p.name
}
```

### 3b. API proxy routes

Create under `maichess-client/app/api/analysis/`:

- `games/route.ts` — GET (list, proxies `?page=&page_size=`) + POST (import PGN)
- `games/[id]/route.ts` — GET (detail) + DELETE
- `games/from-match/[matchId]/route.ts` — POST (from-match import)

Pattern: follow existing API route handlers in `app/api/` exactly. Each handler:
1. Reads `access_token` cookie via `cookies()`.
2. Returns 401 if no token.
3. Forwards the request to `${process.env.ANALYSIS_SERVICE_URL}/games/...` with
   `Authorization: Bearer <token>`.
4. Returns the upstream response (status + body).

Add `ANALYSIS_SERVICE_URL=http://analysis-service` to any `.env` example file.

### 3c. `lib/hooks/useAnalysis.ts` (new file)

```typescript
'use client'

// Manages a WebSocket connection to the Analysis WS service.
// Returns startAnalysis, stopAnalysis, and current analysis state.
```

Implement as a custom hook:
- Open a WebSocket to `${process.env.NEXT_PUBLIC_ANALYSIS_WS_URL}/analysis?token=<jwt>` on mount.
  Read the JWT from the `access_token` cookie (accessible client-side since it is NOT httpOnly for
  this use case — verify against the actual cookie config; if httpOnly, expose the token via a
  `/api/auth/token` route that returns it for the WS handshake).
- State: `lines: AnalysisLine[]`, `depth: number`, `complete: boolean`, `error: string | null`,
  `running: boolean`.
- `startAnalysis(fen: string, botId: string, lineCount: number)`: reset state, send
  `{ type: 'start_analysis', fen, bot_id: botId, line_count: lineCount }`.
- `stopAnalysis()`: send `{ type: 'stop_analysis' }`, set `running = false`.
- Handle incoming messages: dispatch on `type`, update state accordingly.
- On unmount: close the WebSocket.

### 3d. `lib/constants/routes.ts`

Add `analysis: '/analysis'` and `analysisGame: (id: string) => \`/analysis/${id}\`` to the
`ROUTES` constant. Read the file before editing.

### 3e. `app/analysis/page.tsx` (new server component)

Fetch the first page of saved games via `fetch('/api/analysis/games', { ... })` using the
`access_token` cookie (same pattern as `app/match/[id]/page.tsx`). Redirect to login if no token.

Render a list of game cards showing:
- Source badge (PGN / Match)
- White vs Black player names (use `playerInfoDisplayName`)
- Result (`1-0`, `0-1`, `1/2-1/2`, `*`)
- Move count
- Date
- Link to `/analysis/[id]`

Include an "Import PGN" button/form (can be a `<details>` element or a modal) that POSTs to
`/api/analysis/games` and refreshes the list on success.

### 3f. `app/analysis/[id]/page.tsx` (new server component)

Fetch the game detail. Redirect to `/analysis` on 404.

Pass the `AnalysisGame` to `<AnalysisClient game={game} />`.

### 3g. `lib/components/AnalysisClient.tsx` (new client component)

Interactive analysis viewer. Props: `{ game: AnalysisGame }`.

State:
- `positionIndex: number` — current position (0 = starting position, N = after N-th move)
- Analysis state from `useAnalysis` hook

UI layout (follow `MatchClient.tsx` structure):
- Left column: `ChessBoard` — pass `game.fens[positionIndex - 1]` (or starting FEN if index=0),
  `orientation='white'`, `disabled={true}`, no move handlers, no legal move highlights.
- Navigation controls (below board): `← Prev` / `Next →` buttons, current move display
  (`game.moves[positionIndex - 1]` or "Start").
- Right sidebar:
  - Player names and result header.
  - `MoveList` — enhance to accept an `activeIndex` prop so the active move is highlighted.
    Pass `positionIndex` as `activeIndex`. Clicking a row navigates to that position.
  - Analysis controls: "Analyse position" button (when not running) / "Stop" button (when running).
    Show the selected engine name if multiple bots are available (default to `"stockfish-5"` or
    the first available bot — hardcode for now, with a TODO comment to fetch from `/api/bots`).
  - `EvaluationBar` — shown below the analysis button when `lines.length > 0`.
  - `AnalysisLines` — shown below the eval bar when `lines.length > 0`.

When the user clicks "Analyse position":
1. Call `startAnalysis(currentFen, botId, 3)`.
2. Disable position navigation while analysis is running (analysis is position-specific).
3. Navigating to a different position stops any running analysis.

### 3h. `lib/components/EvaluationBar.tsx` (new component)

Visual centipawn bar. Props: `{ evaluationCp: number; isWhiteTurn: boolean }`.

- Map centipawn to a 0–100 percentage: `50 + 50 * Math.tanh(evaluationCp / 400)`.
- White's advantage fills from the bottom; black's from the top.
- Show the numeric evaluation as `+2.4` / `-1.1` / `0.0` (divide cp by 100, two decimal places).

### 3i. `lib/components/AnalysisLines.tsx` (new component)

Props: `{ lines: AnalysisLine[]; depth: number }`.

Render each line as a row: rank badge, evaluation (formatted as above), move sequence in UCI
notation (a TODO comment noting SAN conversion is not yet implemented).

### 3j. "Save for Analysis" button in `GameStatus.tsx`

Read `lib/components/GameStatus.tsx` first.

When the match has `analyzable === true` (prop passed from `MatchClient`), add a "Save for
Analysis" button below the status text. On click:
1. POST to `/api/analysis/games/from-match/${matchId}`.
2. On success (`201`): redirect to `/analysis/${savedGameId}`.
3. On error: show a brief inline error message.

Pass `matchId` and `analyzable` into `GameStatus` from `MatchClient`. Read `MatchClient.tsx`
before modifying it to add the two props.

---

## Verification

After completing all steps:

1. **Analysis service**: `dotnet test -p:CollectCoverage=true` passes, 100% coverage on
   non-excluded code, no build warnings (warnings are errors).
2. **Client**: `npm run lint` passes with no errors.
3. **WS service**: `npx tsc --noEmit` passes.

---

## Environment Variables Summary

**`services/maichess-analysis-service/.env`**
```
Services__DatabaseService=http://match-db:50051
Services__EngineService=http://engine:50051
Services__MatchManagerService=http://match-manager:50051
Services__MoveValidatorService=http://move-validator:50051
Jwt__Key=<same key as other services>
```

**`services/maichess-analysis-ws-service/.env`**
```
PORT=3001
AUTH_SERVICE_GRPC_ADDR=auth:50051
ANALYSIS_SERVICE_GRPC_ADDR=analysis:50051
```

**`maichess-client/.env.local`**
```
ANALYSIS_SERVICE_URL=http://analysis-service
NEXT_PUBLIC_ANALYSIS_WS_URL=ws://localhost:3001
```
