FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CjmApplyApi.csproj .
RUN dotnet restore CjmApplyApi.csproj

COPY . .
RUN dotnet publish CjmApplyApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV CJM_APPLY_DB=/mnt/pipeline/cjm-apply.db
EXPOSE 8080

COPY --from=build /app .
ENTRYPOINT ["dotnet", "CjmApplyApi.dll"]
