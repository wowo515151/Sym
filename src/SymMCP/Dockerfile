FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/SymMCP/SymMCP.csproj", "src/SymMCP/"]
COPY ["src/SymSolvers/SymSolvers.csproj", "src/SymSolvers/"]
COPY ["src/Sym/Sym.csproj", "src/Sym/"]
COPY ["src/SymRules/SymRules.csproj", "src/SymRules/"]
COPY ["src/WordsToSym/WordsToSym.csproj", "src/WordsToSym/"]
COPY ["src/CSharpIO/CSharpIO.csproj", "src/CSharpIO/"]
COPY ["src/SymCore/SymCore.csproj", "src/SymCore/"]
COPY ["src/SymCobra/SymCobra.csproj", "src/SymCobra/"]
COPY ["src/Directory.Build.props", "src/"]

RUN dotnet restore "src/SymMCP/SymMCP.csproj"

COPY . .

RUN dotnet publish "src/SymMCP/SymMCP.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV SymMcp__McpPath=/mcp
ENV SymMcp__DefaultSolveTimeoutSeconds=60
ENV SymMcp__MaxSolveTimeoutSeconds=180

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SymMCP.dll"]
