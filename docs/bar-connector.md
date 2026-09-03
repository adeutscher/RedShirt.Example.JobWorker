# Bar connector

The Bar connector projects (`Connectors.Bar.Core` and `Connectors.Bar.Implementation`) are a placeholder for an
OAuth-backed HTTP API, such as the [RedShirt.Example.Api](https://github.com/adeutscher/RedShirt.Example.Api) template.
They are a generic placeholder for a real API client that a JobWorker uses to perform long-running or downstream work
against another service.

## OAuth by default

This sample connector assumes OAuth 2.0 client-credentials authentication. Authentication in this JobWorker template was
made with OAuth in mind because it supports the authorization model that
the [API Template](https://github.com/adeutscher/RedShirt.Example.Api) is set up for.

If you are planning to use this template with an API that instead uses static keys, the request handler implementation
should be pivoted to be more like the `FooConnector` in
the [API Template](https://github.com/adeutscher/RedShirt.Example.Api).

## Rate limits and reasons to wait

The client respects `BarReasonToWaitException` (including its inheritors `BarRateLimitedException` and
`BarTemporarilyUnavailableException`, both defined in `Connectors.Bar.Implementation`) **indefinitely**. When a call
receives a reason to wait, the connector delays using `ISleepService` for the value of `RetryAfter` when present, or a
configurable fallback (default **15 seconds**
when that fallback is itself `null`). It then retries until the operation succeeds or the job's cancellation token is
triggered. Cancellation and overall job lifetime are the Core job worker configuration's concern. The Bar connector's
duty is to keep trying respectfully when rate limited or otherwise told to wait.

## Last-mile instructions

These steps cannot be fully performed in a general template. Adapt them for your target API.

### If you are using the [API Template](https://github.com/adeutscher/RedShirt.Example.Api)

1. Adjust your API implementation to publish an interop package to a NuGet repository, for example:
    * [Azure DevOps Artifacts](https://learn.microsoft.com/en-us/azure/devops/artifacts/nuget/publish)
    * [GitHub Packages](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
    * [Sonatype Nexus Repository](https://help.sonatype.com/en/nuget-repositories.html)
2. Rename `Bar.Core` and `Bar.Implementation` projects as appropriate for your target API.
3. Reference the interop NuGet package in your renamed implementation project.
4. Write wrapper clients for the relevant clients from the interop package that inspect thrown `SwaggerException`
   instances:
    * `SwaggerException` instances with an HTTP **429** status code (Too Many Requests) should specifically become a
      `BarRateLimitedException` with the value of the `Retry-After` header.
    * Confirm that the exception arbiter (`BarExceptionArbiterService`) treats other status code values appropriately.
5. Adjust your client factory to return the wrapper client.
6. Rename classes whose names begin with `Bar` as appropriate for your target API.

### If you are not using the [API Template](https://github.com/adeutscher/RedShirt.Example.Api) or other OpenAPI package

If you are not using the API template or another flavour of OpenAPI/Swagger-generated package, then the existing Bar
connector example might already be closer to your needs:

1. Rename `Bar.Core` and `Bar.Implementation` projects as appropriate for your target API.
2. Confirm that the exception arbiter treats the appropriate return codes appropriately.
3. In the client response handler, confirm that an HTTP **429** should specifically become a `BarRateLimitedException`
   with the value of the `Retry-After` header.
4. Rename classes whose names begin with `Bar` as appropriate for your target API.

## Local testing

See `test/local/readme.md` for WireMock Bar stubs, SSM/Key Vault credential paths, and OAuth rotation scripts under
`test/local/scripts/wiremock-bar/`.
