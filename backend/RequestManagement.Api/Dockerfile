# API image. Build context: ./backend (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first so the package layer is cached until the project file changes.
COPY RequestManagement.Api/RequestManagement.Api.csproj RequestManagement.Api/
RUN dotnet restore RequestManagement.Api/RequestManagement.Api.csproj
COPY RequestManagement.Api/ RequestManagement.Api/
RUN dotnet publish RequestManagement.Api/RequestManagement.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Non-root user provided by the official .NET images.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "RequestManagement.Api.dll"]
