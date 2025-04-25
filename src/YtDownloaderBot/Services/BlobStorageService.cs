using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace YtDownloaderBot.Services;

public class BlobStorageService
{
    public readonly BlobContainerClient Container;

    public BlobStorageService(string connectionString, string containerName)
    {
        var service = new BlobServiceClient(connectionString);
        Container = service.GetBlobContainerClient(containerName);
        Container.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> UploadFileAsync(string filePath, string blobName)
    {
        var blob = Container.GetBlobClient(blobName);
        await using var stream = File.OpenRead(filePath);
        await blob.UploadAsync(stream, overwrite: true);
        return blob.Uri.ToString();
    }
}