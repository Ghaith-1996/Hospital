FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/CriticalAlerts.Worker/CriticalAlerts.Worker.csproj --locked-mode
RUN dotnet publish src/backend/CriticalAlerts.Worker/CriticalAlerts.Worker.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.10 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CriticalAlerts.Worker.dll"]
