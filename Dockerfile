FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

ARG GITHUB_ACTOR

COPY nuget.config ./
COPY maichess-analysis-service.csproj ./
RUN --mount=type=secret,id=GITHUB_TOKEN \
    GITHUB_TOKEN=$(cat /run/secrets/GITHUB_TOKEN) \
    dotnet restore maichess-analysis-service.csproj

COPY Domain/ Domain/
COPY Data/ Data/
COPY Services/ Services/
COPY Rest/ Rest/
COPY Grpc/ Grpc/
COPY Program.cs ./
RUN dotnet publish maichess-analysis-service.csproj \
    -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "maichess-analysis-service.dll"]
