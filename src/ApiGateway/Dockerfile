FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["global.json", "Directory.Packages.props", "./"]
COPY ["src/ApiGateway/ApiGateway.csproj", "src/ApiGateway/"]

RUN dotnet restore "src/ApiGateway/ApiGateway.csproj"

COPY ["src/ApiGateway/", "src/ApiGateway/"]

RUN dotnet publish "src/ApiGateway/ApiGateway.csproj" \
    --configuration Release \
    --no-restore \
    --output /out \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /out .
USER app

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
