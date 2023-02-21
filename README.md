# EPiServer.Amazon

## What is this project about?
This project consists of Event and Blob providers for running Content Cloud in Amazon Web Services.

## Prerequisites
[Optimzely CMS equal or greater than 12](https://world.optimizely.com/products/#contentmanagement)

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

## License
Distributed under the Apache-2.0 license.
