# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ImageCdn.sln ./
COPY src/ImageCdn.Api/ImageCdn.Api.csproj src/ImageCdn.Api/
COPY tests/ImageCdn.Api.Tests/ImageCdn.Api.Tests.csproj tests/ImageCdn.Api.Tests/
RUN dotnet restore ImageCdn.sln

COPY . .
RUN dotnet publish src/ImageCdn.Api/ImageCdn.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV IMAGE_PROVIDER=Local
ENV LOCAL_IMAGE_ROOT=/data/images
EXPOSE 8080
COPY --from=build /app/publish .
VOLUME ["/data/images"]
ENTRYPOINT ["dotnet", "ImageCdn.Api.dll"]
