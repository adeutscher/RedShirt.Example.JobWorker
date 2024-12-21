FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-noble-chiseled AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY . /build
WORKDIR /build
RUN dotnet restore
RUN dotnet build
ARG TESTS_ENABLE=1
RUN \[ ${TESTS_ENABLE} -ne 1 \] \
  || ( \
    \[ -d "test" \] \
    && failedTestProjects=0 \
    && for testFile in $(find test/ -iname '*csproj'); do \
      if ! dotnet test "${testFile}"; then \
          failedTestProjects=$((failedTestProjects+1)); break; \
      fi; \
    done \
    && [ "${failedTestProjects:-0}" -eq 0 ] \
  )

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN ls -l
RUN ls -l src/
RUN dotnet publish "src/RedShirt.Example.JobWorker/RedShirt.Example.JobWorker.csproj" --self-contained -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./RedShirt.Example.JobWorker"]
