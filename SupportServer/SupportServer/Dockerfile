FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SupportServer.csproj ./
RUN dotnet restore SupportServer.csproj

COPY . ./
RUN dotnet publish SupportServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data && chown -R app:app /app

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
USER app
ENTRYPOINT ["dotnet", "SupportServer.dll"]
