using System.Text.RegularExpressions;
using Agristream.Web.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Agristream.Web.Services;

public record ImageItem(string Name, string FieldId, string UploadedBy, long SizeBytes, DateTimeOffset UploadedUtc, string AuditStatus);

public partial class ImageService
{
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/tiff", "image/webp"];

    [GeneratedRegex("[^a-zA-Z0-9._-]")]
    private static partial Regex UnsafeChars();

    private readonly StorageClients _storage;

    public ImageService(StorageClients storage)
    {
        _storage = storage;
    }

    /// <summary>Uploads a drone image straight to Blob storage; nothing is written to local disk.</summary>
    public async Task<string> UploadAsync(IFormFile file, string fieldId, string uploadedBy, CancellationToken ct)
    {
        if (!AllowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only JPEG, PNG, TIFF or WebP images are accepted.");
        }

        var original = UnsafeChars().Replace(Path.GetFileName(file.FileName), "_");
        var blobName = $"{fieldId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}-{original}";

        var blob = _storage.Images().GetBlobClient(blobName);
        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
            Metadata = new Dictionary<string, string>
            {
                ["fieldId"] = fieldId,
                ["uploadedBy"] = uploadedBy
            }
        }, ct);

        return blobName;
    }

    /// <summary>Uploads in-memory bytes (used by the drone-burst simulator).</summary>
    public async Task UploadBytesAsync(string blobName, byte[] bytes, string contentType, string fieldId, string uploadedBy, CancellationToken ct)
    {
        var blob = _storage.Images().GetBlobClient(blobName);
        await blob.UploadAsync(new BinaryData(bytes), new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = new Dictionary<string, string> { ["fieldId"] = fieldId, ["uploadedBy"] = uploadedBy }
        }, ct);
    }

    public async Task<IReadOnlyList<ImageItem>> ListRecentAsync(int take, CancellationToken ct)
    {
        var blobs = new List<BlobItem>();
        await foreach (var item in _storage.Images().GetBlobsAsync(BlobTraits.Metadata, cancellationToken: ct))
        {
            blobs.Add(item);
            if (blobs.Count >= 500)
            {
                break;
            }
        }

        var recent = blobs.OrderByDescending(b => b.Properties.CreatedOn).Take(take).ToList();
        var audits = await LoadAuditStatusAsync(ct);

        return recent.Select(b =>
        {
            b.Metadata.TryGetValue("fieldId", out var field);
            b.Metadata.TryGetValue("uploadedBy", out var by);
            return new ImageItem(
                b.Name,
                field ?? "unknown",
                by ?? "unknown",
                b.Properties.ContentLength ?? 0,
                b.Properties.CreatedOn ?? DateTimeOffset.MinValue,
                audits.TryGetValue(b.Name, out var status) ? status : "Pending");
        }).ToList();
    }

    public async Task<BlobDownloadStreamingResult?> OpenAsync(string name, CancellationToken ct)
    {
        var blob = _storage.Images().GetBlobClient(name);
        if (!await blob.ExistsAsync(ct))
        {
            return null;
        }

        return (await blob.DownloadStreamingAsync(cancellationToken: ct)).Value;
    }

    private async Task<Dictionary<string, string>> LoadAuditStatusAsync(CancellationToken ct)
    {
        var table = _storage.Table(StorageNames.AuditTable);
        var result = new Dictionary<string, string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var day in new[] { now, now.AddDays(-1) })
        {
            var partition = Partitioning.AuditPartition(day);
            await foreach (var row in table.QueryAsync<ImageAuditEntity>(
                filter: $"PartitionKey eq '{partition}'", maxPerPage: 500, cancellationToken: ct))
            {
                result[row.BlobName] = row.Status;
                if (result.Count >= 1000)
                {
                    return result;
                }
            }
        }

        return result;
    }
}
