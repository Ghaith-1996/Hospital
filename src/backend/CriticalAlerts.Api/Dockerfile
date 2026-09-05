FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj --locked-mode
RUN dotnet publish src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "CriticalAlerts.Api.dll"]
