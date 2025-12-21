# RedShirt.Example.JobWorker

General template for polling/processing messages out of a variety of message stores. 

Repo features:

* Initialisation script for quick namespace adjustment.
* Configuration is based on environment variables.
* Message polling with:
    * [Amazon SQS](https://aws.amazon.com/sqs/)
    * [Amazon Kinesis](https://aws.amazon.com/kinesis/data-streams/)
    * [Apache ActiveMQ Artemis](https://artemis.apache.org/components/artemis/)
    * [NATS](https://nats.io/)
    * [RabbitMQ](https://www.rabbitmq.com/)

# Initialisation

Recommended steps when using this as a template:

1. To change the namespace of the API en-masse for your purposes, use the `init-repo.sh` script:

    ```bash
    bash init-repo.sh New.Namespace.Here
    ```

2. In the `Core` project, update the `IJobDataModel` interface to fit your needs for your project.
3. In the `Implementation.JobManagement.Common` project, update the `JobDataModel` implementation of `IJobDataModel` to
   reflect your changes to the interface.
4. In the `Implementation.JobManagement.Common` project, update `SourceMessageConverter` and `SourceMessageSorter` to
   fit your needs for your project.
5. Update the `Core.Logic` project to handle `IJobDataModel` jobs as needed by your project.
6. Select a message source type and remove the projects for the sources that you are not using.

# Testing

For local testing, see the `test/local/` folder.
