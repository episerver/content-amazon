$ErrorActionPreference = "Stop"

# Set location to the Solution directory
(Get-Item $PSScriptRoot).Parent.FullName | Push-Location
[xml] $versionFile = Get-Content "./src/EPiServer.Amazon/EPiServer.Amazon.csproj"

$node = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='AWSSDK.S3']")
$blobVersion = $node.Attributes["Version"].Value
$parts = $blobVersion.Split(".")
$major = [int]::Parse($parts[0]) + 1
$blobNextMajorVersion = ($major.ToString() + ".0.0") 

$notificationNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='AWSSDK.SimpleNotificationService']")
$notificationVersion = $notificationNode.Attributes["Version"].Value
$notificationParts = $notificationVersion.Split(".")
$notificationMajor = [int]::Parse($notificationParts[0]) + 1
$notificationNextMajorVersion = ($notificationMajor.ToString() + ".0.0") 

$sqsNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='AWSSDK.SQS']")
$sqsVersion = $sqsNode.Attributes["Version"].Value
$sqsParts = $sqsVersion.Split(".")
$sqsMajor = [int]::Parse($sqsParts[0]) + 1
$sqsNextMajorVersion = ($sqsMajor.ToString() + ".0.0") 

$frameworkNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='EPiServer.Framework']")
$frameworkVersion = $frameworkNode.Attributes["Version"].Value
$frameworkParts = $frameworkVersion.Split(".")
$frameworkMajor = [int]::Parse($frameworkParts[0]) + 1
$frameworkNextMajorVersion = ($frameworkMajor.ToString() + ".0.0") 

$diNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='Microsoft.Extensions.DependencyInjection.Abstractions']")
$diVersion = $diNode.Attributes["Version"].Value

$logNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='Microsoft.Extensions.Logging.Abstractions']")
$logVersion = $logNode.Attributes["Version"].Value

$optionsNode = $versionFile.SelectSingleNode("Project/ItemGroup/PackageReference[@Include='Microsoft.Extensions.Options']")
$optionsVersion = $optionsNode.Attributes["Version"].Value

$versionSuffix = $Env:isProduction -eq 'true' ? '' : '-pre-' + $Env:buildNumber
[xml] $versionFile = Get-Content "./build/version.props"
$version = $versionFile.SelectSingleNode("Project/PropertyGroup/VersionPrefix").InnerText + $versionSuffix

dotnet pack --no-restore --no-build -c Release /p:PackageVersion=$version /p:BlobVersion=$blobVersion /p:BlobNextMajorVersion=$blobNextMajorVersion /p:NotificationVersion=$notificationVersion /p:NotificationNextMajorVersion=$notificationNextMajorVersion /p:SqsVersion=$sqsVersion /p:SqsNextMajorVersion=$sqsNextMajorVersion /p:FrameworkVersion=$frameworkVersion /p:FrameworkNextMajorVersion=$frameworkNextMajorVersion /p:DiVersion=$diVersion /p:DiNextMajorVersion=$diNextMajorVersion /p:LogVersion=$logVersion /p:LogNextMajorVersion=$logNextMajorVersion /p:OptionsVersion=$optionsVersion /p:OptionsNextMajorVersion=$optionsNextMajorVersion EPiServer.Amazon.sln

Pop-Location