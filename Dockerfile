FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /build
COPY src src
COPY test test
COPY *.sln .
COPY global.json .

RUN dotnet restore
RUN dotnet build
ARG TESTS_ENABLE=1
RUN \[ ${TESTS_ENABLE} -ne 1 \] \
  || \
      ([ -d "test" \] \
      && dotnet test )

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN rm -rf test *sln global.json \
  && dotnet publish "src/RedShirt.Example.JobWorker/RedShirt.Example.JobWorker.csproj" --self-contained -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./RedShirt.Example.JobWorker"]
