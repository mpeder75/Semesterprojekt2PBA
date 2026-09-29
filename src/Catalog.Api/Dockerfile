FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Packages.props ./
COPY src/Catalog.Contracts/ src/Catalog.Contracts/
COPY src/Catalog.Api/ src/Catalog.Api/
RUN dotnet publish src/Catalog.Api/Catalog.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Catalog.Api.dll"]
