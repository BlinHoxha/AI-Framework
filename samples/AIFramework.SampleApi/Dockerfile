FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/AIFramework.SampleApi/ samples/AIFramework.SampleApi/
RUN dotnet publish samples/AIFramework.SampleApi/AIFramework.SampleApi.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out/ ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AIFramework.SampleApi.dll"]
