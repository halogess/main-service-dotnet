FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5062

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["main-service.csproj", "./"]
RUN dotnet restore "main-service.csproj"
COPY . .
RUN dotnet build "main-service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "main-service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "main-service.dll"]
