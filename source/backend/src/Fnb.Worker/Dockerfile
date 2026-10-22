# syntax=docker/dockerfile:1
# Build context: source/backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props nuget.config Fnb.sln ./
COPY src/Fnb.Domain/Fnb.Domain.csproj src/Fnb.Domain/
COPY src/Fnb.Contracts/Fnb.Contracts.csproj src/Fnb.Contracts/
COPY src/Fnb.Application/Fnb.Application.csproj src/Fnb.Application/
COPY src/Fnb.Infrastructure/Fnb.Infrastructure.csproj src/Fnb.Infrastructure/
COPY src/Fnb.Worker/Fnb.Worker.csproj src/Fnb.Worker/
RUN dotnet restore src/Fnb.Worker/Fnb.Worker.csproj
COPY src/ src/
RUN dotnet publish src/Fnb.Worker/Fnb.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
ENV DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Fnb.Worker.dll"]
