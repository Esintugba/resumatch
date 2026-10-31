# Pinned to the newest image in the 10.0.1xx band allowed by global.json, because
# the floating 10.0 tag moves to newer bands. global.json is copied first so the
# build fails if the SDK falls outside that band.
FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
WORKDIR /src
COPY global.json ./
COPY API/API.csproj API/
RUN dotnet restore API/API.csproj
COPY API/ API/
RUN dotnet publish API/API.csproj --configuration Release --no-restore \
    --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Workstation GC keeps the heap small enough for a 512 MB instance.
ENV DOTNET_gcServer=0
COPY --from=build /app ./
USER app
# Render provides PORT at runtime; 8080 is the image default for other hosts.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=\"${PORT:-8080}\" exec dotnet API.dll"]
