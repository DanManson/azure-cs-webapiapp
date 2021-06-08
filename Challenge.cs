using Pulumi;
using Pulumi.AzureNative.Insights;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;

class Challenge : Stack

{
    Pulumi.Config _config;

    public Challenge()
    {
        _config = new Config("dpm");

        var resourceGroup = new ResourceGroup($"{_config.Require("baseName")}-rg-",new ResourceGroupArgs
        {
            Location = _config.Get("location") ?? "eastus",
        });
        this.ResourceGroupName = resourceGroup.Name;

        var storageAccount = new StorageAccount($"{_config.Require("baseName").ToLower()}", new StorageAccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "StorageV2",
            Sku = new SkuArgs
            {
                Name = SkuName.Standard_LRS,
            },
        });

        var appServicePlan = new AppServicePlan($"{_config.Require("baseName")}-asp-", new AppServicePlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "App",
            Sku = new SkuDescriptionArgs
            {
                Name = "S1",
                Tier =  "Standard",
                Size =  "S1",
                Family =  "S",
                Capacity = 1
            },
        });

        var container = new BlobContainer("zips", new BlobContainerArgs
        {
            AccountName = storageAccount.Name,
            PublicAccess = PublicAccess.None,
            ResourceGroupName = resourceGroup.Name,
        });

        var apiblob = new Blob($"{_config.Require("baseName")}-api", new BlobArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = storageAccount.Name,
            ContainerName = container.Name,
            Type = BlobType.Block,
            Source = new FileArchive(_config.Require("apiDistPath")),
        });
        var apiSasUrl = SignedBlobReadUrl(apiblob, container, storageAccount, resourceGroup);

        var appblob = new Blob($"{_config.Require("baseName")}-app", new BlobArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = storageAccount.Name,
            ContainerName = container.Name,
            Type = BlobType.Block,
            Source = new FileArchive(_config.Require("appDistPath")),
        });
        var appSasUrl = SignedBlobReadUrl(appblob, container, storageAccount, resourceGroup);

        var appInsights = new Component($"{_config.Require("baseName")}-appi-", new ComponentArgs
        {
            ApplicationType = "web",
            Kind = "web",
            ResourceGroupName = resourceGroup.Name,
        });

        var webApi = new WebApp($"{_config.Require("baseName")}-webapi-", new WebAppArgs
        {
            ResourceGroupName = resourceGroup.Name,
            ServerFarmId = appServicePlan.Id,
            SiteConfig = new SiteConfigArgs
            {
                AppSettings =
                {
                    new NameValuePairArgs {Name = "WEBSITE_RUN_FROM_PACKAGE", Value = apiSasUrl},
                },
                AlwaysOn = true,
                NetFrameworkVersion = "v5.0",
            }
        });

        var webApp = new WebApp($"{_config.Require("baseName")}-webapp-", new WebAppArgs
        {
            ResourceGroupName = resourceGroup.Name,
            ServerFarmId = appServicePlan.Id,
            SiteConfig = new SiteConfigArgs
            {
                AppSettings =
                {
                    new NameValuePairArgs {Name = "WEBSITE_RUN_FROM_PACKAGE", Value = appSasUrl},
                    new NameValuePairArgs {Name = "API_URI", Value = Output.Format($"https://{webApi.DefaultHostName}/WeatherForecast")},
                },
                AlwaysOn = true,
                NetFrameworkVersion = "v5.0",
            }
        });

        this.ApiHost = Output.Format($"https://{webApi.DefaultHostName}/swagger");
        this.AppHost = Output.Format($"https://{webApp.DefaultHostName}/Index.html");
    }

    private static Output<string> SignedBlobReadUrl(Blob blob, BlobContainer container, StorageAccount account, ResourceGroup resourceGroup)
    {
        return Output.Tuple<string, string, string, string>(
            blob.Name, container.Name, account.Name, resourceGroup.Name).Apply(t =>
        {
            (string blobName, string containerName, string accountName, string resourceGroupName) = t;

            var blobSAS = ListStorageAccountServiceSAS.InvokeAsync(new ListStorageAccountServiceSASArgs
            {
                AccountName = accountName,
                Protocols = HttpProtocol.Https,
                SharedAccessStartTime = "2021-01-01",
                SharedAccessExpiryTime = "2030-01-01",
                Resource = SignedResource.C,
                ResourceGroupName = resourceGroupName,
                Permissions = Permissions.R,
                CanonicalizedResource = "/blob/" + accountName + "/" + containerName,
                ContentType = "application/json",
                CacheControl = "max-age=5",
                ContentDisposition = "inline",
                ContentEncoding = "deflate",
            });
            return Output.Format($"https://{accountName}.blob.core.windows.net/{containerName}/{blobName}?{blobSAS.Result.ServiceSasToken}");
        });
    }

    [Output] public Output<string>? ApiHost { get; set; }
    [Output] public Output<string>? AppHost { get; set; }
    [Output] public Output<string>? ResourceGroupName { get; set;}
}