# Secret Managers

This general template has support for using a secret manager service. The services within the template interact with the
secret manager through the `ISecretManagerService` or `ISecretManagerCacheService` interfaces.
`ISecretManagerCacheService` maintains an in-memory cache of secrets in order to avoid overwhelming the secret manager
service by accident.

At the moment, there are three available implementations of `ISecretManagerService`:

* [AWS SSM Parameter Store](https://docs.aws.amazon.com/systems-manager/latest/userguide/systems-manager-parameter-store.html)
* [Azure Key Vault](https://azure.microsoft.com/en-us/products/key-vault)
* [Docker Secrets](https://docs.docker.com/reference/compose-file/secrets/)
  ([Secondary Link](https://docs.docker.com/engine/swarm/secrets/))

The Core library of this general template indirectly makes use of `ISecretManagerService`, requiring it to be configured
in dependency injection by default. This general template assumes that the chosen secret manager implementation is SSM.
The exception to this is if the chosen job source is either Azure Queue Storage or Azure Service Bus job sources, which
configures Azure Key Vault as the secret manager. The Azure-based job sources use Key Vault with the assumption that
mixing major cloud platforms would be unusual. The template chooses a secret manager provider in the
`Extensions/ServiceCollectionExtensions.cs` file of the root `RedShirt.Example.JobWorker` project.

Please keep this in mind when adapting this template for your specific application.

The following job source implementations (read: pretty much all of them) rely on a Secret Manager as part of their
operations:

* NATS
* RabbitMQ
* ActiveMQ
* Azure Queue Storage
* Azure Service Bus
* AWS Kinesis (indirectly, for its use of Redis in short-term iterator storage)
* Redis Streams

## Docker Secrets

While the other secret managers are more straightforward with their plans, Docker Secrets has some caveats and
assumptions that should be documented.

In general, I would encourage the use of a secret manager other than Docker Secrets. Compared to other options it lacks
flexibility. However, it may be all that is needed for a small-scale environment and leaves the architecture open to
be pivoted to a different implementation.

Other notes:

* Unlike other secret managers, a container instance's secrets cannot be rotated without restarting the container.
* Secrets are assumed to be files in `/run/secrets`.
    * This directory can be overridden by setting a new path in `COMMON__SECRETS__DOCKER__DIRECTORY`
* Secret keys are assumed to be roughly equal to the underlying file specified in the Docker stack configuration. If the
  file does not meet these guidelines because the compose file specifies another target path within the container, then
  the secret manager has no information with which to resolve a key to a file containing a value. After checking the
  exact name of the key under the secret directory, the secret manager will attempt the path with a few file extensions
  (though realistically, only the flat key match will probably be useful).
    * For example, with no overriding directory the key `foo-password` will be searched for under the following absolute
      paths:
        * `/run/secrets/foo-password`
        * `/run/secrets/foo-password.txt`
        * `/run/secrets/foo-password.json`
    * In general, it's advised not to meddle with secret targets within the container at all if you plan to use them
      with this template's Docker secret manager.
* The implementation has not been tested under Docker Swarm, and as such hasn't been tested with external secrets.