# Build context: repository root  (docker build -f src/ClaimFlow.Api/Dockerfile .)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes
COPY global.json Directory.Build.props ./
COPY src/ClaimFlow.Domain/ClaimFlow.Domain.csproj src/ClaimFlow.Domain/
COPY src/ClaimFlow.Application/ClaimFlow.Application.csproj src/ClaimFlow.Application/
COPY src/ClaimFlow.Infrastructure/ClaimFlow.Infrastructure.csproj src/ClaimFlow.Infrastructure/
COPY src/ClaimFlow.Api/ClaimFlow.Api.csproj src/ClaimFlow.Api/
RUN dotnet restore src/ClaimFlow.Api/ClaimFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/ClaimFlow.Api/ClaimFlow.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Non-root user that ships with the .NET 8 images
USER $APP_UID
ENTRYPOINT ["dotnet", "ClaimFlow.Api.dll"]
