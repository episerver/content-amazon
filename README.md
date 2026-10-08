# EPiServer.Amazon

## What is this project about?
This project consists of Event and Blob providers for running Content Cloud in Amazon Web Services.

## Prerequisites
[Optimizely CMS equal or greater than 13](https://world.optimizely.com/products/#contentmanagement)

## Installation
To install add to startup file in ConfigureServices section.
```csharp
const string userBucketName = "CHANGE_ME";
const string userTopicName = "CHANGE_ME";
const string userRegion = "CHANGE_ME";
const string userAccessKey = "CHANGE_ME";
const string userSecretKey = "CHANGE_ME";

services.AddAmazonBlobProvider(o => {
	o.BucketName = userBucketName;
	o.AccessKey = userAccessKey;
	o.SecretKey = userSecretKey;
	o.Region = userRegion;
});

services.AddAmazonEventProvider(o => {
	o.TopicName = userTopicName;
	o.AccessKey = userAccessKey;
	o.SecretKey = userSecretKey;
	o.Region = userRegion;
});
```
Note: if the BucketName and TopicName do not exist on AWS, they will be automatically created.

And update connectionstring to AWS RDS (Relational Database Service) in startup file.
```csharp
var connectionstring = _configuration.GetConnectionString("EPiServerDB") ??
               $"Data Source=CHANGE_ME.rds.amazonaws.com,1433;Database=CHANGE_ME;User Id=CHANGE_ME;Password=CHANGE_ME;TrustServerCertificate=True;";
```

or in appsettings.json
```json
{
  //...omitted code
  "ConnectionStrings": {
    "EPiServerDB" : "Data Source=CHANGE_ME.rds.amazonaws.com,1433;Database=CHANGE_ME;User Id=CHANGE_ME;Password=CHANGE_ME;TrustServerCertificate=True;"
  }
}
```

## Alternative installation
In ConfigureServices section, add
```csharp
services.AddAmazonBlobProvider();
services.AddAmazonEventProvider();
```
In appsettings.json
```json
{
  //...omitted code
  "EPiServer": {
	//...omitted code
    "AmazonBlob": {
      "AmazonBlobClient": {
        "BucketName": "CHANGE_ME",
        "AccessKey": "CHANGE_ME",
        "SecretKey": "CHANGE_ME/",
        "Region": "CHANGE_ME"
      }
    },
    "AmazonEvents": {
      "AmazonEventClient": {
        "TopicName": "CHANGE_ME",
        "AccessKey": "CHANGE_ME",
        "SecretKey": "CHANGE_ME/",
        "Region": "CHANGE_ME"
      }
    }
  }
}
```

## Configuration options
Both providers support the following credential settings. If none of them are set, the default AWS SDK for .NET credential resolution is used (environment variables, IAM roles, etc.). `ProfileName` cannot be combined with `AccessKey`/`SecretKey`.

| Option | Description | Default |
| --- | --- | --- |
| `AccessKey` / `SecretKey` | AWS access key credentials. Both must be set together. | _not set_ |
| `ProfileName` | The name of an AWS credential profile to use. | _not set_ |
| `Region` | The AWS region to connect to, e.g. `eu-west-1`. Required. | _not set_ |

Blob provider:

| Option | Description | Default |
| --- | --- | --- |
| `BucketName` | The S3 bucket that blobs are stored in. Required. | _not set_ |
| `DownloadChunkSize` | The size in bytes of each ranged request made when reading a blob. Maximum 4194304 (4 MB). | 2097152 (2 MB) |

Event provider:

| Option | Description | Default |
| --- | --- | --- |
| `TopicName` | The SNS topic that events are published to. Required, and should be unique per environment. | _not set_ |
| `MaxSendThreads` | The maximum number of events that may be published to SNS concurrently. | 1 |
| `MaxEventThreads` | The maximum number of threads used to process received events. | 1 |
| `QueueBatchSize` | The maximum number of messages retrieved from SQS with each request. | 10 |
| `QueueWaitTime` | How long receive requests wait for new messages (SQS long polling). | 20 seconds |
| `QueueExpiration` | The message retention period of the subscription queue. | 2 hours |
| `DeleteQueueLimit` | How old the most recent message of another queue can be before that queue is considered abandoned and is deleted. | 30 minutes |
| `DisableAutoCleanupQueue` | Disables the background cleanup of abandoned queues. | false |

## Custom event types
Event messages are serialized as plain JSON without any embedded type information. When a message is received, the type of its `Parameter` is resolved from the event types registered in `EventsOptions`, keyed by the event id. The CMS registers its own event types automatically, but if your site raises custom remote events whose parameter is a complex object, the parameter type must be registered on every site:

```csharp
services.Configure<EventsOptions>(o => o.AddEventType<MyEventData>(MyEventId));
```

or, if the type is decorated with an `EventDataAttribute`:

```csharp
services.AddCmsEventType<MyEventData>();
```

The parameter of an event that has not been registered is received as `null`. Scalar parameters (strings, numbers, booleans and GUIDs) work without registration.

## Upgrading from version 10
Version 11 targets .NET 10 and CMS 13/Commerce 15, upgrades to AWS SDK for .NET v4, and replaces Newtonsoft.Json with System.Text.Json for event messages.

- **Deploy all instances of an environment together.** The event message format has changed, so servers running version 10 and version 11 cannot reliably exchange event parameters. Until every instance has been upgraded, remote events (such as cache invalidation) may not propagate.
- **Register custom event types.** Messages no longer carry type information; see [Custom event types](#custom-event-types).
- **`MaxSendThreads` now limits publish concurrency.** Publishing used to be fire-and-forget, launching every queued event at SNS at once regardless of this setting. Sends are now capped at `MaxSendThreads` concurrent requests (default 1). If you worked around the old behavior with a custom provider that publishes synchronously, the workaround can be removed. Note that such overrides will no longer compile as-is: `AmazonEventClient.Initialize` and the `MessageSerializer` constructor have new signatures.
- `BoxedValue<T>` has been removed, and the `Serializer` properties of `TopicClient` and `SubscriptionClient` are now read-only.

## License
Distributed under the Apache-2.0 license.
