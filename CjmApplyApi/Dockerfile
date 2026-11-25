# Build from repo root:
#   docker build -f CjmApplyApi/Dockerfile -t cjm-apply-api .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CjmApply.Core/CjmApply.Core.csproj CjmApply.Core/
COPY CjmApplyApi/CjmApplyApi.csproj CjmApplyApi/
RUN dotnet restore CjmApplyApi/CjmApplyApi.csproj

COPY CjmApply.Core/ CjmApply.Core/
COPY CjmApplyApi/ CjmApplyApi/
RUN dotnet publish CjmApplyApi/CjmApplyApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV CJM_APPLY_DB=/mnt/pipeline/cjm-apply.db
EXPOSE 8080

COPY --from=build /app .
ENTRYPOINT ["dotnet", "CjmApplyApi.dll"]
