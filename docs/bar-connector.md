# Bar connector

The Bar connector projects (`Connectors.Bar.Core` and `Connectors.Bar.Implementation`) are a placeholder for an
OAuth-backed HTTP API, such as the [RedShirt.Example.Api](https://github.com/adeutscher/RedShirt.Example.Api) template.
They stand in for a real API client that a JobWorker uses to perform long-running or downstream work against another
service.

## OAuth by default

This sample connector assumes OAuth 2.0 client-credentials authentication. Authentication in this JobWorker template was
made with OAuth in mind due to personal preference for projects and because it supports the authorization model that
the [API Template](https://github.com/adeutscher/RedShirt.Example.Api) is set up for.

If you are planning to use this for an API that instead uses static keys, the request handler implementation should be
pivoted to be more like the `FooConnector` in the [API Template](https://github.com/adeutscher/RedShirt.Example.Api).

## Rate limits and reasons to wait

The client respects `BarReasonToWaitException` (including `BarRateLimitedException` and
`BarTemporarilyUnavailableException`, both defined in `Connectors.Bar.Implementation`) **indefinitely**. When a call
receives a reason to wait, the connector delays using `ISleepService` for the value of `RetryAfter` when present, or a
configurable fallback (default **15 seconds**
when that fallback is null). It then retries until the operation succeeds or the job's cancellation token is triggered.
Cancellation and overall job lifetime are the Core job worker configuration's concern; the Bar connector's duty is to
keep trying respectfully when rate limited or otherwise told to wait.

## Last-mile instructions

These steps cannot be fully prescribed by a general template. Adapt them for your target API.

### If you are using the [API Template](https://github.com/adeutscher/RedShirt.Example.Api)

1. Adjust your API implementation to publish an interop package to a NuGet repository, for example:
    - [Azure DevOps Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/publish)
    - [GitHub Packages](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
    - [Sonatype Nexus Repository](https://help.sonatype.com/en/nuget-repositories.html)
2. Rename `Bar.Core` and `Bar.Implementation` projects as appropriate for your target API.
3. Reference the interop NuGet package in your renamed implementation project.
4. Write wrapper clients for the relevant clients from the interop package that translate thrown `SwaggerException`
   instances to a generic `BarException` (renamed for your API):
    - `BarException` translation should judge the status code in the `SwaggerException` to set `CouldBeTransient`.
      Abstract this decision into an exception arbiter service with a method called `CouldSwaggerExceptionBeTransient`.
    - The exception to this translation is HTTP **429**, which should specifically become a `BarRateLimitedException`
      with the value of the `Retry-After` header.
5. Adjust your client factory to return the wrapper client.

### If you are not using the [API Template](https://github.com/adeutscher/RedShirt.Example.Api)

1. Rename `Bar.Core` and `Bar.Implementation` projects as appropriate for your target API.
2. Adjust HTTP clients for the subject API so non-successful status codes translate to a generic `BarException`:
    - Judge the status code to set `CouldBeTransient`. Abstract this into an exception arbiter service with a method
      called `CouldSwaggerExceptionBeTransient` (or an equivalent name if you are not using Swagger/OpenAPI clients).
    - HTTP **429** should specifically become a `BarRateLimitedException` with the value of the `Retry-After` header.
3. Rename classes whose names begin with `Bar` as appropriate for your target API.

## Local testing

See `test/local/readme.md` for WireMock Bar stubs, SSM/Key Vault credential paths, and OAuth rotation scripts under
`test/local/scripts/wiremock-bar/`.
